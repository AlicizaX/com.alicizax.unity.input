#if INPUTSYSTEM_SUPPORT
namespace AlicizaX.UI.UXNavigation
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UI;
    using AlicizaX.UI.Runtime;

    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Navigation Scope")]
    public sealed class NavigationScope : MonoBehaviour, UnityEngine.ISerializationCallbackReceiver
    {
        [SerializeField, Header("默认选中控件")] private Selectable _defaultSelectable;

        [SerializeField, Header("所属 UIHolder"), Tooltip("必须绑定所属 UI：Window/Widget 绑定自身 Holder，普通物体绑定所属父级 UI 的 Holder。")]
        private UIHolderObjectBase _holder;

        [SerializeField, Header("编辑器烘焙导航控件")] private Selectable[] _bakedSelectables = System.Array.Empty<Selectable>();
        [SerializeField, Header("记住上次选中")] private bool _rememberLastSelection = false;

        [SerializeField, Header("阻断下层导航域")] private bool _blockLowerScopes = true;
        [SerializeField, Header("可作为导航焦点")] private bool _navigable = true;
        [SerializeField, HideInInspector] private bool _hasNavigableField;

        // 烘焙控件与运行时注册控件合并到同一组容器：一份归属集合、一份基线、一份顺序。
        private readonly List<Selectable> _selectables = new List<Selectable>(16);
        private readonly List<Navigation> _baseline = new List<Navigation>(16);
        private readonly List<bool> _rememberable = new List<bool>(16);
        private readonly HashSet<Selectable> _owned = new HashSet<Selectable>();

        private Canvas _cachedSortingCanvas;
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
        internal int BakedSelectableCount => _bakedSelectables != null ? _bakedSelectables.Length : 0;
        public int RuntimeSelectableCount => _selectables.Count - _bakedCount;

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

        public bool Navigable
        {
            get => _navigable;
            set
            {
                if (_navigable == value)
                {
                    return;
                }

                _navigable = value;
                _hasNavigableField = true;
                NotifySelectableStateChanged();
            }
        }

        internal UIHolderObjectBase Holder => _holder;

        /// <summary>所属 UI 层级中实际决定排序的 Canvas。</summary>
        internal Canvas SortingCanvas
        {
            get
            {
                if (_cachedSortingCanvas == null)
                    _cachedSortingCanvas = InputHierarchyUtility.ResolveSortingCanvas(GetComponentInParent<Canvas>(true));
                return _cachedSortingCanvas;
            }
        }

        private void OnEnable()
        {
            InitializeLists();
            _cachedSortingCanvas = null;
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
            _cachedSortingCanvas = null;
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

            _selectables.Add(selectable);
            _baseline.Add(selectable.navigation);
            _rememberable.Add(rememberable);
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
            int index = _selectables.IndexOf(selectable);
            if (index < _bakedCount)
            {
                return false;
            }

            if (_navigationSuppressed)
            {
                selectable.navigation = _baseline[index];
            }

            RemoveEntryAt(index);
            if (ReferenceEquals(_lastSelected, selectable))
            {
                _lastSelected = null;
            }

            NotifySelectableStateChanged();
            return true;
        }

        internal int GetHierarchyDepth()
        {
            if (_cachedHierarchyDepth >= 0)
            {
                return _cachedHierarchyDepth;
            }

            _cachedHierarchyDepth = InputHierarchyUtility.GetDepth(transform);
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
            return _owned.Contains(selectable) ? selectable : null;
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
        }

        public void NotifySelectableStateChanged()
        {
            UXInput.Navigation?.Refresh();
        }

        public void OnBeforeSerialize()
        {
            _hasNavigableField = true;
        }

        public void OnAfterDeserialize()
        {
            if (_hasNavigableField)
            {
                return;
            }

            // 旧资产没有该字段，默认值 false 会把域设为不可导航；迁移为旧版本的隐性默认 true。
            _navigable = true;
            _hasNavigableField = true;
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
                    _selectables.Add(selectable);
                    _baseline.Add(selectable.navigation);
                    _rememberable.Add(true);
                }
            }

            _bakedCount = _selectables.Count;
        }

        private void RemoveEntryAt(int index)
        {
            _owned.Remove(_selectables[index]);
            _selectables.RemoveAt(index);
            _baseline.RemoveAt(index);
            _rememberable.RemoveAt(index);
        }

        private void ApplySuppression(bool suppressed)
        {
            for (int i = 0; i < _selectables.Count; i++)
            {
                Selectable selectable = _selectables[i];
                if (selectable == null)
                {
                    continue;
                }

                if (suppressed)
                {
                    _baseline[i] = selectable.navigation;
                    SetSelectableSuppressed(selectable);
                }
                else
                {
                    selectable.navigation = _baseline[i];
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
            if (selectable == null)
            {
                return false;
            }

            int index = _selectables.IndexOf(selectable);
            return index >= 0 && _rememberable[index];
        }

        private Selectable FindFirstAvailableSelectable()
        {
            for (int i = 0; i < _selectables.Count; i++)
            {
                Selectable selectable = _selectables[i];
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
