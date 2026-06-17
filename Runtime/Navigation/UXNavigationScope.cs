#if INPUTSYSTEM_SUPPORT
namespace AlicizaX.UI.UXNavigation
{
    using UnityEngine;
    using UnityEngine.UI;
    using AlicizaX.UI.Runtime;

    [DisallowMultipleComponent]
    [AddComponentMenu("UI/UX Navigation Scope")]
    public sealed class UXNavigationScope : MonoBehaviour
    {
        private const int InvalidIndex = -1;
        [SerializeField, Header("默认选中控件")] private Selectable _defaultSelectable;

        [SerializeField, Header("所属 UIHolder")]
        private UIHolderObjectBase _holder;

        [SerializeField, Header("编辑器烘焙导航控件")] private Selectable[] _bakedSelectables = System.Array.Empty<Selectable>();
        [SerializeField, Header("记住上次选中")] private bool _rememberLastSelection = false;

        [SerializeField, Header("阻断下层导航域")] private bool _blockLowerScopes = true;

        private Selectable[] _runtimeSelectables;
        private Navigation[] _bakedBaselineNavigation;
        private Navigation[] _runtimeBaselineNavigation;
        private bool[] _runtimeSelectableRememberable;
        private Canvas _cachedCanvas;
        private Selectable _lastSelected;
        private bool _navigationSuppressed;
        private int _cachedHierarchyDepth = -1;
        private bool _cachedIsSkipped;
        private bool _isSkippedCacheValid;
        private int _runtimeSelectableCount;
        private int[] _bakedSelectableHashIds = System.Array.Empty<int>();
        private int[] _bakedSelectableHashIndices = System.Array.Empty<int>();
        private int[] _runtimeSelectableHashIds = System.Array.Empty<int>();
        private int[] _runtimeSelectableHashIndices = System.Array.Empty<int>();
        private bool _selectableSetDirty = true;
        private bool _selectableAvailabilityDirty = true;
        private int _availableSelectableCount;
        private Selectable _firstAvailableSelectable;
        private UIHolderObjectBase _subscribedHolder;
        private Coroutine _refreshAfterEnableCoroutine;

        internal int RuntimeIndex { get; set; } = InvalidIndex;
        internal ulong ActivationSerial { get; set; }
        internal bool IsAvailable { get; set; }
        internal bool WasAvailable { get; set; }
        public bool NavigationSuppressed => _navigationSuppressed;
        internal int BakedSelectableCount => _bakedSelectables != null ? _bakedSelectables.Length : 0;
        public int RuntimeSelectableCount => _runtimeSelectableCount;
        public int RuntimeSelectableCapacity => _runtimeSelectables != null ? Mathf.Max(_runtimeSelectables.Length, BakedSelectableCount) : BakedSelectableCount;

        public Selectable DefaultSelectable
        {
            get => _defaultSelectable;
            set
            {
                _defaultSelectable = value;
                MarkSelectableAvailabilityDirty();
                MarkRuntimeStateDirty();
            }
        }

        public bool RememberLastSelection => _rememberLastSelection;
        public bool BlockLowerScopes => _blockLowerScopes;

        internal bool IsNavigationSkipped
        {
            get
            {
                if (!_isSkippedCacheValid)
                {
                    _cachedIsSkipped = HasSkipInParents();
                    _isSkippedCacheValid = true;
                }

                return _cachedIsSkipped;
            }
        }

        private bool HasSkipInParents()
        {
            Transform current = transform;
            while (current != null)
            {
                if (current.TryGetComponent(out UXNavigationSkip skip) && skip.isActiveAndEnabled)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
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

        internal UIHolderObjectBase Holder
        {
            get => _holder;
        }

        private void Awake()
        {
            EnsureRuntimeBuffers(false);
            RefreshBaselineWhenUnsuppressed();
            SubscribeHolderEvents();
            UXNavigationSystem.RegisterScope(this);
        }

        private void OnEnable()
        {
            MarkSelectableAvailabilityDirty();
            _refreshAfterEnableCoroutine = StartCoroutine(RefreshNavigationAfterEnable());
        }

        private void OnDisable()
        {
            if (_refreshAfterEnableCoroutine != null)
            {
                StopCoroutine(_refreshAfterEnableCoroutine);
                _refreshAfterEnableCoroutine = null;
            }

            MarkSelectableAvailabilityDirty();
            UXNavigationSystem.RequestRefresh(true);
        }

        private System.Collections.IEnumerator RefreshNavigationAfterEnable()
        {
            yield return null;
            _refreshAfterEnableCoroutine = null;
            MarkSelectableAvailabilityDirty();
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
            MarkSelectableAvailabilityDirty();
            MarkRuntimeStateDirty();
        }

        private void OnTransformParentChanged()
        {
            _cachedCanvas = null;
            _cachedHierarchyDepth = -1;
            _isSkippedCacheValid = false;
            MarkRuntimeStateDirty();
        }

        public bool RegisterSelectable(Selectable selectable, bool rememberable = false)
        {
            if (selectable == null || !Owns(selectable.gameObject) || ContainsSelectable(selectable))
            {
                return false;
            }

            EnsureRuntimeBuffers(true);
            if (_runtimeSelectableCount >= _runtimeSelectables.Length)
            {
                EnsureRuntimeCapacity(_runtimeSelectableCount + 1);
            }

            int index = _runtimeSelectableCount++;
            _runtimeSelectables[index] = selectable;
            _runtimeBaselineNavigation[index] = selectable.navigation;
            _runtimeSelectableRememberable[index] = rememberable;
            MarkSelectableSetDirty();
            if (_navigationSuppressed)
            {
                SetSelectableSuppressed(selectable);
            }

            MarkRuntimeStateDirty();
            return true;
        }

        public bool UnregisterSelectable(Selectable selectable)
        {
            if (selectable == null || _runtimeSelectables == null)
            {
                return false;
            }

            int index = FindRuntimeIndex(selectable);
            if (index < 0)
            {
                return false;
            }

            if (_navigationSuppressed)
            {
                selectable.navigation = _runtimeBaselineNavigation[index];
            }

            int last = --_runtimeSelectableCount;
            Selectable movedSelectable = _runtimeSelectables[last];
            Navigation movedNavigation = _runtimeBaselineNavigation[last];
            bool movedRememberable = _runtimeSelectableRememberable[last];
            _runtimeSelectables[last] = null;
            _runtimeBaselineNavigation[last] = default(Navigation);
            _runtimeSelectableRememberable[last] = false;
            if (index != last)
            {
                _runtimeSelectables[index] = movedSelectable;
                _runtimeBaselineNavigation[index] = movedNavigation;
                _runtimeSelectableRememberable[index] = movedRememberable;
            }

            if (_lastSelected == selectable)
            {
                _lastSelected = null;
            }

            MarkSelectableSetDirty();
            MarkRuntimeStateDirty();
            return true;
        }

        public void InvalidateSelectableCache()
        {
            if (_navigationSuppressed)
            {
                ApplySuppression(_bakedSelectables, _bakedBaselineNavigation, BakedSelectableCount, false);
                ApplySuppression(_runtimeSelectables, _runtimeBaselineNavigation, _runtimeSelectableCount, false);
                CaptureBaselineBeforeSuppress();
                ApplySuppression(_bakedSelectables, _bakedBaselineNavigation, BakedSelectableCount, true);
                ApplySuppression(_runtimeSelectables, _runtimeBaselineNavigation, _runtimeSelectableCount, true);
            }
            else
            {
                RefreshBaselineWhenUnsuppressed();
            }

            MarkRuntimeStateDirty();
        }

        internal void InvalidateSkipCacheOnly()
        {
            _isSkippedCacheValid = false;
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
            MarkSelectableAvailabilityDirty();
            UXNavigationSystem.RequestRefresh(true);
        }

        private void OnWindowClosed()
        {
            MarkSelectableAvailabilityDirty();
            UXNavigationSystem.RequestRefresh(true);
        }

        internal int GetHierarchyDepth()
        {
            if (_cachedHierarchyDepth >= 0)
            {
                return _cachedHierarchyDepth;
            }

            int depth = 0;
            Transform current = transform;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }

            _cachedHierarchyDepth = depth;
            return _cachedHierarchyDepth;
        }

        internal bool Owns(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            UXNavigationScope nearestScope = target.GetComponentInParent<UXNavigationScope>(true);
            return nearestScope == this;
        }

        internal Selectable GetPreferredSelectable()
        {
            RefreshSelectableAvailabilityIfDirty();
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
            RefreshSelectableAvailabilityIfDirty();
            if (IsSelectableValid(_defaultSelectable))
            {
                return true;
            }

            return _availableSelectableCount > 0;
        }

        internal bool HasValidDefaultSelectable()
        {
            return IsSelectableValid(_defaultSelectable);
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

        internal bool IsSelectableOwnedAndValid(GameObject selectedObject)
        {
            return IsSelectableValid(GetSelectableFromObject(selectedObject));
        }

        internal void SetNavigationSuppressed(bool suppressed)
        {
            if (_navigationSuppressed == suppressed)
            {
                return;
            }

            if (suppressed)
            {
                CaptureBaselineBeforeSuppress();
            }

            _navigationSuppressed = suppressed;
            ApplySuppression(_bakedSelectables, _bakedBaselineNavigation, BakedSelectableCount, suppressed);
            ApplySuppression(_runtimeSelectables, _runtimeBaselineNavigation, _runtimeSelectableCount, suppressed);
        }

        private void EnsureRuntimeBuffers(bool preserveRuntimeSelectables)
        {
            int capacity = _runtimeSelectables != null ? Mathf.Max(_runtimeSelectables.Length, BakedSelectableCount) : BakedSelectableCount;
            ResizeRuntimeBuffers(capacity, preserveRuntimeSelectables);

            int bakedCount = BakedSelectableCount;
            if (_bakedBaselineNavigation == null || _bakedBaselineNavigation.Length != bakedCount)
            {
                _bakedBaselineNavigation = bakedCount > 0 ? new Navigation[bakedCount] : System.Array.Empty<Navigation>();
                CreateBakedHash(bakedCount);
                MarkSelectableSetDirty();
            }
        }

        private void ResizeRuntimeBuffers(int capacity, bool preserveRuntimeSelectables)
        {
            if (_runtimeSelectables == null
                || _runtimeSelectables.Length != capacity
                || _runtimeBaselineNavigation == null
                || _runtimeBaselineNavigation.Length != capacity
                || _runtimeSelectableRememberable == null
                || _runtimeSelectableRememberable.Length != capacity)
            {
                Selectable[] previousSelectables = _runtimeSelectables;
                Navigation[] previousBaseline = _runtimeBaselineNavigation;
                bool[] previousRememberable = _runtimeSelectableRememberable;
                int previousCount = _runtimeSelectableCount;
                _runtimeSelectables = capacity > 0 ? new Selectable[capacity] : System.Array.Empty<Selectable>();
                _runtimeBaselineNavigation = capacity > 0 ? new Navigation[capacity] : System.Array.Empty<Navigation>();
                _runtimeSelectableRememberable = capacity > 0 ? new bool[capacity] : System.Array.Empty<bool>();
                CreateRuntimeHash(capacity);
                _runtimeSelectableCount = 0;

                if (preserveRuntimeSelectables && previousSelectables != null && capacity > 0)
                {
                    int copyCount = previousCount < capacity ? previousCount : capacity;
                    for (int i = 0; i < copyCount; i++)
                    {
                        Selectable selectable = previousSelectables[i];
                        if (selectable == null)
                        {
                            continue;
                        }

                        _runtimeSelectables[_runtimeSelectableCount] = selectable;
                        _runtimeBaselineNavigation[_runtimeSelectableCount] = previousBaseline != null && i < previousBaseline.Length
                            ? previousBaseline[i]
                            : selectable.navigation;
                        _runtimeSelectableRememberable[_runtimeSelectableCount] = previousRememberable != null && i < previousRememberable.Length && previousRememberable[i];
                        _runtimeSelectableCount++;
                    }
                }

                MarkSelectableSetDirty();
            }
        }

        private void EnsureRuntimeCapacity(int required)
        {
            if (_runtimeSelectables != null && required <= _runtimeSelectables.Length)
            {
                return;
            }

            int capacity = _runtimeSelectables != null ? _runtimeSelectables.Length : 0;
            if (capacity <= 0)
            {
                capacity = Mathf.Max(BakedSelectableCount, 1);
            }

            while (capacity < required)
            {
                capacity <<= 1;
            }

            ResizeRuntimeBuffers(capacity, true);
        }

        private void CaptureBaselineBeforeSuppress()
        {
            EnsureRuntimeBuffers(true);
            RefreshSelectableHashesIfDirty();
            CaptureBaseline(_bakedSelectables, _bakedBaselineNavigation, BakedSelectableCount);
            CaptureBaseline(_runtimeSelectables, _runtimeBaselineNavigation, _runtimeSelectableCount);
        }

        private void RefreshBaselineWhenUnsuppressed()
        {
            if (_navigationSuppressed)
            {
                return;
            }

            CaptureBaselineBeforeSuppress();
        }

        private static void CaptureBaseline(Selectable[] selectables, Navigation[] baseline, int count)
        {
            if (selectables == null || baseline == null)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Selectable selectable = selectables[i];
                if (selectable != null)
                {
                    baseline[i] = selectable.navigation;
                }
            }
        }

        private static void ApplySuppression(Selectable[] selectables, Navigation[] baseline, int count, bool suppressed)
        {
            if (selectables == null || baseline == null)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Selectable selectable = selectables[i];
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
                    selectable.navigation = baseline[i];
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
            if (selectable == null)
            {
                return false;
            }

            RefreshSelectableHashesIfDirty();
            int instanceId = selectable.GetInstanceID();
            return FindHashIndex(_bakedSelectableHashIds, _bakedSelectableHashIndices, instanceId) >= 0
                   || FindHashIndex(_runtimeSelectableHashIds, _runtimeSelectableHashIndices, instanceId) >= 0;
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

            RefreshSelectableHashesIfDirty();
            int instanceId = selectable.GetInstanceID();
            if (FindHashIndex(_bakedSelectableHashIds, _bakedSelectableHashIndices, instanceId) >= 0)
            {
                return true;
            }

            int runtimeIndex = FindHashIndex(_runtimeSelectableHashIds, _runtimeSelectableHashIndices, instanceId);
            return runtimeIndex >= 0
                   && _runtimeSelectableRememberable != null
                   && runtimeIndex < _runtimeSelectableRememberable.Length
                   && _runtimeSelectableRememberable[runtimeIndex];
        }

        private void MarkSelectableSetDirty()
        {
            _selectableSetDirty = true;
            MarkSelectableAvailabilityDirty();
        }

        private void MarkSelectableAvailabilityDirty()
        {
            _selectableAvailabilityDirty = true;
        }

        public void NotifySelectableStateChanged()
        {
            MarkSelectableAvailabilityDirty();
            MarkRuntimeStateDirty();
        }

        private void RefreshSelectableAvailabilityIfDirty()
        {
            if (!_selectableAvailabilityDirty)
            {
                return;
            }

            _availableSelectableCount = 0;
            _firstAvailableSelectable = null;
            AccumulateAvailableSelectables(_bakedSelectables, BakedSelectableCount);
            AccumulateAvailableSelectables(_runtimeSelectables, _runtimeSelectableCount);
            _selectableAvailabilityDirty = false;
        }

        private void AccumulateAvailableSelectables(Selectable[] selectables, int count)
        {
            if (selectables == null)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Selectable selectable = selectables[i];
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
        }

        private void RefreshSelectableHashesIfDirty()
        {
            if (!_selectableSetDirty)
            {
                return;
            }

            RebuildBakedHash();
            RebuildRuntimeHash();
            _selectableSetDirty = false;
        }

        private void RebuildBakedHash()
        {
            ClearHash(_bakedSelectableHashIds, _bakedSelectableHashIndices);
            for (int i = 0; i < BakedSelectableCount; i++)
            {
                Selectable selectable = _bakedSelectables[i];
                if (selectable != null)
                {
                    AddHash(_bakedSelectableHashIds, _bakedSelectableHashIndices, selectable.GetInstanceID(), i);
                }
            }
        }

        private void RebuildRuntimeHash()
        {
            ClearHash(_runtimeSelectableHashIds, _runtimeSelectableHashIndices);
            for (int i = 0; i < _runtimeSelectableCount; i++)
            {
                Selectable selectable = _runtimeSelectables[i];
                if (selectable != null)
                {
                    AddHash(_runtimeSelectableHashIds, _runtimeSelectableHashIndices, selectable.GetInstanceID(), i);
                }
            }
        }

        private void CreateBakedHash(int itemCapacity)
        {
            int hashCapacity = GetHashCapacity(itemCapacity);
            _bakedSelectableHashIds = hashCapacity > 0 ? new int[hashCapacity] : System.Array.Empty<int>();
            _bakedSelectableHashIndices = hashCapacity > 0 ? new int[hashCapacity] : System.Array.Empty<int>();
        }

        private void CreateRuntimeHash(int itemCapacity)
        {
            int hashCapacity = GetHashCapacity(itemCapacity);
            _runtimeSelectableHashIds = hashCapacity > 0 ? new int[hashCapacity] : System.Array.Empty<int>();
            _runtimeSelectableHashIndices = hashCapacity > 0 ? new int[hashCapacity] : System.Array.Empty<int>();
        }

        private static int GetHashCapacity(int itemCapacity)
        {
            if (itemCapacity <= 0)
            {
                return 0;
            }

            int hashCapacity = 1;
            int required = itemCapacity << 1;
            while (hashCapacity < required)
            {
                hashCapacity <<= 1;
            }

            return hashCapacity;
        }

        private static void ClearHash(int[] ids, int[] indices)
        {
            if (ids == null || indices == null)
            {
                return;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = 0;
                indices[i] = InvalidIndex;
            }
        }

        private static int FindHashIndex(int[] ids, int[] indices, int instanceId)
        {
            if (ids == null || indices == null || ids.Length == 0 || instanceId == 0)
            {
                return InvalidIndex;
            }

            int mask = ids.Length - 1;
            int index = instanceId & mask;
            for (int i = 0; i < ids.Length; i++)
            {
                int storedId = ids[index];
                if (storedId == 0)
                {
                    return InvalidIndex;
                }

                if (storedId == instanceId)
                {
                    return indices[index];
                }

                index = (index + 1) & mask;
            }

            return InvalidIndex;
        }

        private static void AddHash(int[] ids, int[] indices, int instanceId, int selectableIndex)
        {
            if (ids == null || indices == null || ids.Length == 0 || instanceId == 0)
            {
                return;
            }

            int mask = ids.Length - 1;
            int index = instanceId & mask;
            for (int i = 0; i < ids.Length; i++)
            {
                int storedId = ids[index];
                if (storedId == 0 || storedId == instanceId)
                {
                    ids[index] = instanceId;
                    indices[index] = selectableIndex;
                    return;
                }

                index = (index + 1) & mask;
            }
        }

        private int FindRuntimeIndex(Selectable selectable)
        {
            if (selectable == null)
            {
                return InvalidIndex;
            }

            RefreshSelectableHashesIfDirty();
            return FindHashIndex(_runtimeSelectableHashIds, _runtimeSelectableHashIndices, selectable.GetInstanceID());
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

        private void MarkRuntimeStateDirty()
        {
            UXNavigationSystem.MarkStateDirty();
        }
    }
}
#endif
