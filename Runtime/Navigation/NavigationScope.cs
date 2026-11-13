#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.Input
{
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Navigation Scope")]
    public sealed class NavigationScope : MonoBehaviour
    {
        [SerializeField, Header("默认选中控件")] private Selectable _defaultSelectable;

        [SerializeField, Header("所属 UIHolder"), Tooltip("必须绑定所属 UI：Window/Widget 绑定自身 Holder，普通物体绑定所属父级 UI 的 Holder。")]
        private UIHolderObjectBase _holder;

        [SerializeField, Header("编辑器烘焙导航控件")] private Selectable[] _bakedSelectables = System.Array.Empty<Selectable>();
        [SerializeField, Header("记住上次选中")] private bool _rememberLastSelection = false;

        [SerializeField, Header("阻断下层导航域")] private bool _blockLowerScopes = true;

        /// <summary>一个受管控件：归属、抑制前的导航基线、是否允许跨关闭/遮挡记忆，放在同一条记录里。</summary>
        private struct Entry
        {
            public Selectable Selectable;
            public Navigation Baseline;
            public bool Rememberable;
        }

        // 烘焙控件在前（_bakedCount 个），运行时注册的在后；_owned 只做 O(1) 归属判断。
        private readonly List<Entry> _entries = new List<Entry>(16);
        private readonly HashSet<Selectable> _owned = new HashSet<Selectable>();

        private InputSortingCanvas _sortingCanvas;
        private Selectable _lastSelected;
        private bool _navigationSuppressed;
        private int _cachedHierarchyDepth = -1;
        private int _bakedCount;
        private UIHolderObjectBase _subscribedHolder;
        private bool _listsBuilt;
#if UNITY_EDITOR
        private bool _missingUnregisterReported;
#endif

        internal int RuntimeIndex { get; set; } = -1;
        internal ulong ActivationSerial { get; set; }
        internal bool IsAlive { get; set; }
        internal bool IsNavigationAllowed { get; set; }
        public bool NavigationSuppressed => _navigationSuppressed;

        /// <summary>抑制状态变化时触发（仅在值真正变化时）。参数为新的抑制状态。</summary>
        public event Action<bool> NavigationSuppressionChanged;

        internal int BakedSelectableCount => _bakedSelectables != null ? _bakedSelectables.Length : 0;
        public int RuntimeSelectableCount => _entries.Count - _bakedCount;

        public Selectable DefaultSelectable
        {
            get => _defaultSelectable;
            set
            {
                if (ReferenceEquals(_defaultSelectable, value)) return;
                _defaultSelectable = value;
                NotifySelectableStateChanged();
            }
        }

        public bool RememberLastSelection => _rememberLastSelection;
        public bool BlockLowerScopes => _blockLowerScopes;

        internal UIHolderObjectBase Holder => _holder;

        /// <summary>所属 UI 层级中实际决定排序的 Canvas。</summary>
        internal Canvas SortingCanvas => _sortingCanvas.Get(this);

        private void OnEnable()
        {
            InitializeLists();
            _sortingCanvas.Invalidate();
            _cachedHierarchyDepth = -1;
            SubscribeHolderEvents();
            UXInput.Navigation?.RegisterScope(this);
        }

        private void OnDisable()
        {
            UnsubscribeHolderEvents();
            UXInput.Navigation?.UnregisterScope(this);
        }

        private void OnTransformChildrenChanged()
        {
            NotifySelectableStateChanged();
        }

        private void OnTransformParentChanged()
        {
            _sortingCanvas.Invalidate();
            _cachedHierarchyDepth = -1;
            NotifySelectableStateChanged();
        }

        /// <summary>
        /// 注册运行时控件。rememberable 只控制跨关闭/遮挡边界的记忆；当前导航期间始终记录选择。
        /// 烘焙控件已进入同一列表，重复注册返回 false。
        /// </summary>
        public bool RegisterSelectable(Selectable selectable, bool rememberable = false)
        {
            InitializeLists();
            if (selectable == null || !Owns(selectable.gameObject) || !_owned.Add(selectable))
            {
                return false;
            }

            _entries.Add(new Entry { Selectable = selectable, Baseline = selectable.navigation, Rememberable = rememberable });
            if (_navigationSuppressed)
            {
                SetSelectableSuppressed(selectable);
            }

            NotifySelectableStateChanged();
            return true;
        }

        /// <summary>
        /// 注销运行时控件。烘焙控件不可注销，返回 false。
        /// </summary>
        public bool UnregisterSelectable(Selectable selectable)
        {
            if (selectable == null)
            {
                return false;
            }

            InitializeLists();
            if (!_owned.Contains(selectable))
            {
                return false;
            }

            int index = IndexOf(selectable);
            if (index < _bakedCount)
            {
                return false;
            }

            if (_navigationSuppressed)
            {
                selectable.navigation = _entries[index].Baseline;
            }

            _owned.Remove(selectable);
            _entries.RemoveAt(index);
            if (ReferenceEquals(_lastSelected, selectable))
            {
                _lastSelected = null;
            }

            NotifySelectableStateChanged();
            return true;
        }

        internal int GetHierarchyDepth()
        {
            if (_cachedHierarchyDepth < 0)
            {
                _cachedHierarchyDepth = InputHierarchyUtility.GetDepth(transform);
            }

            return _cachedHierarchyDepth;
        }

        private bool Owns(GameObject target)
        {
            NavigationScope nearestScope = target.GetComponentInParent<NavigationScope>(true);
            return ReferenceEquals(nearestScope, this);
        }

        internal Selectable GetPreferredSelectable()
        {
            if (IsSelectableValid(_lastSelected))
            {
                return _lastSelected;
            }

            if (IsSelectableValid(_defaultSelectable))
            {
                return _defaultSelectable;
            }

            return FindFirstAvailableSelectable();
        }

        internal Selectable GetOwnedSelectable(GameObject selectedObject)
        {
            Selectable selectable = _lastSelected != null && _lastSelected.gameObject == selectedObject
                ? _lastSelected : GetSelectableFromObject(selectedObject);
            return selectable != null && _owned.Contains(selectable) ? selectable : null;
        }

        internal void RecordSelection(Selectable selectable) => _lastSelected = selectable;

        internal void EndNavigation()
        {
            if (!_rememberLastSelection || !IsSelectableRememberable(_lastSelected))
                _lastSelected = null;
        }

        internal void SetNavigationSuppressed(bool suppressed)
        {
            if (_navigationSuppressed == suppressed)
            {
                return;
            }

            _navigationSuppressed = suppressed;
            ApplySuppression(suppressed);
            NavigationSuppressionChanged?.Invoke(suppressed);
        }

        public void NotifySelectableStateChanged()
        {
            UXInput.Navigation?.Refresh();
        }

        private void InitializeLists()
        {
            if (_listsBuilt)
            {
                return;
            }

            _listsBuilt = true;
            for (int i = 0; i < BakedSelectableCount; i++)
            {
                Selectable selectable = _bakedSelectables[i];
                if (selectable != null && _owned.Add(selectable))
                {
                    _entries.Add(new Entry { Selectable = selectable, Baseline = selectable.navigation, Rememberable = true });
                }
            }

            _bakedCount = _entries.Count;
        }

        private int IndexOf(Selectable selectable)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (ReferenceEquals(_entries[i].Selectable, selectable))
                {
                    return i;
                }
            }

            return -1;
        }

        private void ApplySuppression(bool suppressed)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (entry.Selectable == null)
                {
                    continue;
                }

                if (suppressed)
                {
                    entry.Baseline = entry.Selectable.navigation;
                    _entries[i] = entry;
                    SetSelectableSuppressed(entry.Selectable);
                }
                else
                {
                    entry.Selectable.navigation = entry.Baseline;
                }
            }
        }

        private static void SetSelectableSuppressed(Selectable selectable)
        {
            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        private bool IsSelectableValid(Selectable selectable)
        {
            return IsSelectableUsable(selectable) && _owned.Contains(selectable);
        }

        private bool IsSelectableRememberable(Selectable selectable)
        {
            if (selectable == null || !_owned.Contains(selectable))
            {
                return false;
            }

            int index = IndexOf(selectable);
            return index >= 0 && _entries[index].Rememberable;
        }

        private Selectable FindFirstAvailableSelectable()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Selectable selectable = _entries[i].Selectable;
#if UNITY_EDITOR
                if (selectable == null && i >= _bakedCount && !_missingUnregisterReported)
                {
                    _missingUnregisterReported = true;
                    Debug.Assert(false,
                        "A runtime Selectable was destroyed without UnregisterSelectable. Call UnregisterSelectable before destroying it.",
                        this);
                }
#endif
                if (IsSelectableUsable(selectable)) return selectable;
            }

            return null;
        }

        internal static bool IsSelectableUsable(Selectable selectable)
        {
            return selectable != null && selectable.IsActive() && selectable.IsInteractable();
        }

        private static Selectable GetSelectableFromObject(GameObject selectedObject)
        {
            return selectedObject.TryGetComponent(out Selectable selectable)
                ? selectable
                : selectedObject.GetComponentInParent<Selectable>();
        }

        private void SubscribeHolderEvents()
        {
            UIHolderObjectBase holder = Holder;
            if (holder == null)
            {
                return;
            }

            holder.OnShowEvent += NotifySelectableStateChanged;
            holder.OnCloseEvent += NotifySelectableStateChanged;
            _subscribedHolder = holder;
        }

        private void UnsubscribeHolderEvents()
        {
            if (ReferenceEquals(_subscribedHolder, null))
            {
                return;
            }

            _subscribedHolder.OnShowEvent -= NotifySelectableStateChanged;
            _subscribedHolder.OnCloseEvent -= NotifySelectableStateChanged;
            _subscribedHolder = null;
        }
    }
}
#endif
