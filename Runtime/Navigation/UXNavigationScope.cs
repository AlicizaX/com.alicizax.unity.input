#if INPUTSYSTEM_SUPPORT
namespace AlicizaX.UI.UXNavigation
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UI;
    using AlicizaX.UI.Runtime;

    [DisallowMultipleComponent]
    [AddComponentMenu("UI/UX Navigation Scope")]
    public sealed class UXNavigationScope : MonoBehaviour, UnityEngine.ISerializationCallbackReceiver
    {
        [SerializeField, Header("默认选中控件")] private Selectable _defaultSelectable;

        [SerializeField, Header("所属 UIHolder")]
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

        private Canvas _cachedCanvas;
        private Selectable _lastSelected;
        private bool _navigationSuppressed;
        private int _cachedHierarchyDepth = -1;
        private int _bakedCount;
        private int _runtimeSelectableCount;
        private int _availableSelectableCount;
        private Selectable _firstAvailableSelectable;
        private UIHolderObjectBase _subscribedHolder;
        private bool _refreshAfterEnable;
        private bool _listsBuilt;
        private bool _availabilityDirty = true;
#if UNITY_EDITOR
        private bool _missingUnregisterReported;
#endif

        internal int RuntimeIndex { get; set; } = -1;
        internal ulong ActivationSerial { get; set; }
        internal bool IsAlive { get; set; }
        internal bool WasAlive { get; set; }
        internal bool IsAvailable { get; set; }
        public bool NavigationSuppressed => _navigationSuppressed;
        internal int BakedSelectableCount => _bakedSelectables != null ? _bakedSelectables.Length : 0;
        public int RuntimeSelectableCount => _runtimeSelectableCount;

        public Selectable DefaultSelectable
        {
            get => _defaultSelectable;
            set
            {
                _defaultSelectable = value;
                InvalidateAvailability();
                InvalidateNavigation();
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
                UXNavigationSystem.RequestRefresh(true);
            }
        }

        internal Canvas Canvas
        {
            get
            {
                if (_cachedCanvas == null)
                {
                    _cachedCanvas = GetComponentInParent<Canvas>(true);
                }

                return _cachedCanvas;
            }
        }

        internal UIHolderObjectBase Holder => _holder;

        private void Awake()
        {
            InitializeLists();
            SubscribeHolderEvents();
            UXNavigationSystem.RegisterScope(this);
        }

        private void OnEnable()
        {
            _refreshAfterEnable = true;
            InvalidateAvailability();
            UXNavigationSystem.RequestRefresh(true);
        }

        private void LateUpdate()
        {
            if (!_refreshAfterEnable)
            {
                return;
            }

            _refreshAfterEnable = false;
            InvalidateAvailability();
            UXNavigationSystem.RequestRefresh(true);
        }

        private void OnDisable()
        {
            _refreshAfterEnable = false;
            InvalidateAvailability();
            UXNavigationSystem.RequestRefresh(true);
        }

        private void OnDestroy()
        {
            UnsubscribeHolderEvents();
            SetNavigationSuppressed(false);
            UXNavigationSystem.UnregisterScope(this);
        }

        private void OnTransformChildrenChanged()
        {
            InvalidateAvailability();
            InvalidateNavigation();
        }

        private void OnTransformParentChanged()
        {
            _cachedCanvas = null;
            _cachedHierarchyDepth = -1;
            InvalidateNavigation();
        }

        /// <summary>
        /// 注册运行时控件。烘焙控件在 Awake 时已经进入同一列表，重复注册返回 false。
        /// </summary>
        public bool RegisterSelectable(Selectable selectable, bool rememberable = false)
        {
            InitializeLists();
            if (selectable == null || !Owns(selectable.gameObject) || _owned.Contains(selectable))
            {
                return false;
            }

            AddEntry(selectable, rememberable);
            _runtimeSelectableCount++;
            if (_navigationSuppressed)
            {
                SetSelectableSuppressed(selectable);
            }

            InvalidateAvailability();
            InvalidateNavigation();
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
            if (index < 0 || index < _bakedCount)
            {
                return false;
            }

            if (_navigationSuppressed)
            {
                selectable.navigation = _baseline[index];
            }

            RemoveEntryAt(index);
            _runtimeSelectableCount--;
            if (ReferenceEquals(_lastSelected, selectable))
            {
                _lastSelected = null;
            }

            InvalidateAvailability();
            InvalidateNavigation();
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

        internal bool Owns(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            UXNavigationScope nearestScope = target.GetComponentInParent<UXNavigationScope>(true);
            return ReferenceEquals(nearestScope, this);
        }

        internal Selectable GetPreferredSelectable()
        {
            RecalculateAvailability();
            if (_rememberLastSelection && IsSelectableValid(_lastSelected))
            {
                return _lastSelected;
            }

            if (IsSelectableValid(_defaultSelectable))
            {
                return _defaultSelectable;
            }

            return _firstAvailableSelectable;
        }

        internal bool HasAvailableSelectable()
        {
            RecalculateAvailability();
            if (IsSelectableValid(_defaultSelectable))
            {
                return true;
            }

            return _availableSelectableCount > 0;
        }

        internal void RecordSelection(GameObject selectedObject)
        {
            if (!_rememberLastSelection || selectedObject == null)
            {
                return;
            }

            Selectable selectable = GetSelectableFromObject(selectedObject);
            if (IsSelectableValid(selectable) && IsSelectableRememberable(selectable))
            {
                _lastSelected = selectable;
            }
        }

        internal bool IsOwnedAndUsable(GameObject selectedObject)
        {
            return IsSelectableValid(GetSelectableFromObject(selectedObject));
        }

        internal void SetNavigationSuppressed(bool suppressed)
        {
            if (_navigationSuppressed == suppressed)
            {
                return;
            }

            InitializeLists();
            if (suppressed)
            {
                CaptureBaseline();
            }

            _navigationSuppressed = suppressed;
            ApplySuppression(suppressed);
        }

        public void NotifySelectableStateChanged()
        {
            InvalidateAvailability();
            InvalidateNavigation();
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
            _availabilityDirty = true;
        }

        private void AddEntry(Selectable selectable, bool rememberable)
        {
            _owned.Add(selectable);
            _selectables.Add(selectable);
            _baseline.Add(selectable.navigation);
            _rememberable.Add(rememberable);
        }

        private void RemoveEntryAt(int index)
        {
            _owned.Remove(_selectables[index]);
            _selectables.RemoveAt(index);
            _baseline.RemoveAt(index);
            _rememberable.RemoveAt(index);
        }

        private void CaptureBaseline()
        {
            for (int i = 0; i < _selectables.Count; i++)
            {
                Selectable selectable = _selectables[i];
                if (selectable != null)
                {
                    _baseline[i] = selectable.navigation;
                }
            }
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
            if (selectable == null)
            {
                return;
            }

            Navigation navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        private bool ContainsSelectable(Selectable selectable)
        {
            return selectable != null && _owned.Contains(selectable);
        }

        private bool IsSelectableValid(Selectable selectable)
        {
            return IsSelectableUsable(selectable) && ContainsSelectable(selectable);
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

        private void InvalidateAvailability()
        {
            _availabilityDirty = true;
        }

        private void RecalculateAvailability()
        {
            if (!_availabilityDirty)
            {
                return;
            }

#if UNITY_EDITOR
            if (!_missingUnregisterReported)
            {
                for (int i = _bakedCount; i < _selectables.Count; i++)
                {
                    if (_selectables[i] == null)
                    {
                        _missingUnregisterReported = true;
                        Debug.Assert(
                            false,
                            "A runtime Selectable was destroyed without UnregisterSelectable. Call UnregisterSelectable before destroying it.",
                            this);
                        break;
                    }
                }
            }
#endif

            _availableSelectableCount = 0;
            _firstAvailableSelectable = null;
            for (int i = 0; i < _selectables.Count; i++)
            {
                Selectable selectable = _selectables[i];
                if (!IsSelectableUsable(selectable))
                {
                    continue;
                }

                if (_firstAvailableSelectable == null)
                {
                    _firstAvailableSelectable = selectable;
                }

                _availableSelectableCount++;
            }

            _availabilityDirty = false;
        }

        private static bool IsSelectableUsable(Selectable selectable)
        {
            return selectable != null && selectable.IsActive() && selectable.IsInteractable();
        }

        private static Selectable GetSelectableFromObject(GameObject selectedObject)
        {
            if (selectedObject == null)
            {
                return null;
            }

            return selectedObject.TryGetComponent(out Selectable selectable)
                ? selectable
                : selectedObject.GetComponentInParent<Selectable>();
        }

        private void SubscribeHolderEvents()
        {
            UIHolderObjectBase holder = Holder;
            if (holder == null || _subscribedHolder == holder)
            {
                return;
            }

            UnsubscribeHolderEvents();
            holder.OnWindowAfterShowEvent += OnWindowShown;
            holder.OnWindowAfterClosedEvent += OnWindowClosed;
            _subscribedHolder = holder;
        }

        private void UnsubscribeHolderEvents()
        {
            if (_subscribedHolder == null)
            {
                return;
            }

            _subscribedHolder.OnWindowAfterShowEvent -= OnWindowShown;
            _subscribedHolder.OnWindowAfterClosedEvent -= OnWindowClosed;
            _subscribedHolder = null;
        }

        private void OnWindowShown()
        {
            InvalidateAvailability();
            UXNavigationSystem.RequestRefresh(true);
        }

        private void OnWindowClosed()
        {
            InvalidateAvailability();
            UXNavigationSystem.RequestRefresh(true);
        }

        private void InvalidateNavigation()
        {
            UXNavigationSystem.InvalidateNavigation();
        }
    }
}
#endif
