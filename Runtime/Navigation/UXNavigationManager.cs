#if INPUTSYSTEM_SUPPORT
namespace AlicizaX.UI.UXNavigation
{
    using AlicizaX.UI.Runtime;
    using UnityEngine.InputSystem.UI;
    using UnityEngine.EventSystems;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// 如有需要实现接口对接自己的业务逻辑
    /// </summary>
    public interface IUXNavigationModeChangeProcessor
    {
        void OnNavigationModeChanged(UXInputMode mode, UXNavigationScope previousTopScope, UXNavigationScope currentTopScope);
    }

    [System.Serializable]
    public sealed class DefaultUXNavigationModeChangeProcessor : IUXNavigationModeChangeProcessor
    {
        [InspectorName("键盘模式禁用空白失焦")] [SerializeField]
        private bool m_KeyboardDeselectOnBackgroundClick;

        [InspectorName("手柄模式禁用空白失焦")] [SerializeField]
        private bool m_GamepadDeselectOnBackgroundClick;

        [InspectorName("键盘模式下是否显示鼠标")] [SerializeField]
        private bool m_KeyboardCursorVisible = true;

        [InspectorName("手柄模式下是否显示鼠标")] [SerializeField]
        private bool m_GamepadCursorVisible;

        public void OnNavigationModeChanged(UXInputMode mode, UXNavigationScope previousTopScope, UXNavigationScope currentTopScope)
        {
            ShouldShowCursor(mode);
            SetDeselectOnBackgroundClick((mode == UXInputMode.Gamepad && !m_GamepadDeselectOnBackgroundClick)
                                         || (mode == UXInputMode.Keyboard && !m_KeyboardDeselectOnBackgroundClick));
        }

        private void ShouldShowCursor(UXInputMode mode)
        {
            if (mode == UXInputMode.Gamepad)
            {
                Cursor.visible = m_GamepadCursorVisible;
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (mode == UXInputMode.Keyboard)
            {
                Cursor.visible = m_KeyboardCursorVisible;
            }

            Cursor.lockState = CursorLockMode.None;
        }

        private void SetDeselectOnBackgroundClick(bool value)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }

            InputSystemUIInputModule inputModule = eventSystem.currentInputModule as InputSystemUIInputModule;
            if (inputModule == null)
            {
                inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            }

            if (inputModule != null)
            {
                inputModule.deselectOnBackgroundClick = value;
            }
        }
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("UI/UX Navigation Manager")]
    public sealed class UXNavigationManager : MonoServiceBehaviour<GameplayScope>
    {
        private const int ScopeCapacity = 128;
        private const int InvalidIndex = -1;

        internal static UXNavigationManager Instance;

        private readonly UXNavigationScope[] _scopes = new UXNavigationScope[ScopeCapacity];
        private int _scopeCount;

        [SerializeReference] private IUXNavigationModeChangeProcessor _modeChangeProcessor = new DefaultUXNavigationModeChangeProcessor();
        [SerializeField, HideInInspector] private string _modeChangeProcessorTypeName = typeof(DefaultUXNavigationModeChangeProcessor).FullName;

        private UXNavigationScope _topScope;
        private ulong _activationSerial;
        private bool _stateDirty = true;
        private bool _suppressionDirty = true;
        private bool _isFlushingState;
        private bool _contextNotificationDirty;
        private UXNavigationScope _pendingPreviousTopScope;

        internal static void RequestRefresh(bool ensureSelection)
        {
            if (Instance != null)
            {
                Instance.RequestRefreshInternal(ensureSelection);
            }
        }

        internal static void RequestEnsureSelection()
        {
            if (Instance != null)
            {
                Instance.FlushStateIfDirty(false, true);
            }
        }

        public static bool IsHolderWithinTopScope(UIHolderObjectBase holder)
        {
            if (Instance == null || Instance._topScope == null || holder == null)
            {
                return true;
            }

            UXNavigationScope scope = holder.GetComponentInParent<UXNavigationScope>(true);
            return scope == Instance._topScope;
        }

        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);
        }

        protected override void OnInitialize()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            RegisterLoadedScopes();
        }

        private void OnEnable()
        {
            UXNavigationModeListener.OnModeChanged += OnInputModeChanged;
        }

        private void OnDisable()
        {
            UXNavigationModeListener.OnModeChanged -= OnInputModeChanged;
        }

        protected override void OnDestroyService()
        {
            UXNavigationModeListener.OnModeChanged -= OnInputModeChanged;
            Instance = null;
        }

        internal void RegisterScope(UXNavigationScope scope)
        {
            if (scope == null || scope.RuntimeIndex != InvalidIndex)
            {
                return;
            }

            if (_scopeCount >= _scopes.Length)
            {
                ReportCapacityExceeded();
                return;
            }

            int index = _scopeCount++;
            _scopes[index] = scope;
            scope.RuntimeIndex = index;
            scope.InvalidateSkipCacheOnly();
            MarkStateDirty();
        }

        internal void UnregisterScope(UXNavigationScope scope)
        {
            if (scope == null)
            {
                return;
            }

            int index = scope.RuntimeIndex;
            if (index < 0 || index >= _scopeCount || _scopes[index] != scope)
            {
                return;
            }

            if (_topScope == scope)
            {
                CaptureTopScopeSelection();
                SetTopScope(null, false);
            }

            scope.IsAvailable = false;
            scope.WasAvailable = false;
            scope.SetNavigationSuppressed(false);
            scope.RuntimeIndex = InvalidIndex;

            int last = --_scopeCount;
            UXNavigationScope movedScope = _scopes[last];
            _scopes[last] = null;
            if (index != last)
            {
                _scopes[index] = movedScope;
                movedScope.RuntimeIndex = index;
            }

            MarkStateDirty();
        }

        internal void MarkStateDirty()
        {
            _stateDirty = true;
            _suppressionDirty = true;
        }

        internal void MarkSuppressionDirty()
        {
            _suppressionDirty = true;
        }

        private void RequestRefreshInternal(bool ensureSelection)
        {
            MarkStateDirty();
            FlushStateIfDirty(true, ensureSelection);
        }

        internal void InvalidateSkipCaches()
        {
            for (int i = 0; i < _scopeCount; i++)
            {
                _scopes[i].InvalidateSkipCacheOnly();
            }

            MarkStateDirty();
        }

        private void FlushStateIfDirty(bool notifyContext, bool ensureSelection = true)
        {
            if (_isFlushingState || (!_stateDirty && !_suppressionDirty && !ensureSelection))
            {
                return;
            }

            _isFlushingState = true;
            CaptureTopScopeSelection();
            UXNavigationScope previousTopScope = _topScope;
            if (_stateDirty)
            {
                UXNavigationScope newTopScope = FindTopScope();
                _stateDirty = false;
                SetTopScope(newTopScope, false);
            }

            if (_suppressionDirty)
            {
                ApplyScopeSuppression();
                _suppressionDirty = false;
            }

            if (ensureSelection && ShouldEnsureSelection())
            {
                EnsureNavigationSelection();
            }

            _isFlushingState = false;
            if (notifyContext)
            {
                NotifyContextIfChanged(previousTopScope, _topScope);
            }
            else if (!ReferenceEquals(previousTopScope, _topScope))
            {
                QueueContextNotification(previousTopScope);
            }
        }

        private UXNavigationScope FindTopScope()
        {
            UXNavigationScope bestScope = null;
            for (int i = 0; i < _scopeCount; i++)
            {
                UXNavigationScope scope = _scopes[i];
                bool available = IsScopeAvailable(scope);
                scope.IsAvailable = available;
                if (scope.WasAvailable != available)
                {
                    scope.WasAvailable = available;
                    if (available)
                    {
                        scope.ActivationSerial = ++_activationSerial;
                    }

                    _suppressionDirty = true;
                }

                if (available && (bestScope == null || IsHigherPriority(scope, bestScope)))
                {
                    bestScope = scope;
                }
            }

            return bestScope;
        }

        private static bool IsScopeAvailable(UXNavigationScope scope)
        {
            if (scope == null || !scope.isActiveAndEnabled || !scope.gameObject.activeInHierarchy)
            {
                return false;
            }

            Canvas canvas = scope.Canvas;
            return canvas != null
                   && canvas.gameObject.layer == UIComponent.UIShowLayer
                   && !scope.IsNavigationSkipped
                   && scope.HasAvailableSelectable();
        }

        private void ApplyScopeSuppression()
        {
            for (int i = 0; i < _scopeCount; i++)
            {
                UXNavigationScope scope = _scopes[i];
                bool suppress = scope.IsAvailable
                                && _topScope != null
                                && scope != _topScope
                                && _topScope.BlockLowerScopes
                                && IsHigherPriority(_topScope, scope);
                scope.SetNavigationSuppressed(suppress);
            }
        }

        private void EnsureNavigationSelection()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || _topScope == null || !ShouldEnsureSelection())
            {
                return;
            }

            GameObject currentSelected = eventSystem.currentSelectedGameObject;
            if (_topScope.IsSelectableOwnedAndValid(currentSelected))
            {
                _topScope.RecordSelection(currentSelected);
                return;
            }

            Selectable preferred = _topScope.GetPreferredSelectable();
            eventSystem.SetSelectedGameObject(preferred != null ? preferred.gameObject : null);
            GameObject selectedObject = eventSystem.currentSelectedGameObject;
            if (selectedObject != null)
            {
                _topScope.RecordSelection(selectedObject);
            }
        }

        private void CaptureTopScopeSelection()
        {
            if (_topScope == null)
            {
                return;
            }

            EventSystem eventSystem = EventSystem.current;
            GameObject selectedObject = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (_topScope.IsSelectableOwnedAndValid(selectedObject))
            {
                _topScope.RecordSelection(selectedObject);
            }
        }

        private void OnInputModeChanged(UXInputMode mode)
        {
            UXNavigationScope previousTopScope = _topScope;
            if (ShouldEnsureSelection())
            {
                FlushStateIfDirty(false, true);
            }

            NotifyContextIfChanged(previousTopScope, _topScope);
        }

        private bool ShouldEnsureSelection()
        {
            return UXNavigationModeListener.RequiresSelectedForCurrentMode
                   || (_topScope != null
                       && UXNavigationModeListener.CurrentMode == UXInputMode.Keyboard
                       && _topScope.HasValidDefaultSelectable());
        }

        private void SetTopScope(UXNavigationScope topScope, bool notifyContext)
        {
            if (ReferenceEquals(_topScope, topScope))
            {
                return;
            }

            UXNavigationScope previousTopScope = _topScope;
            CaptureTopScopeSelection();
            _topScope = topScope;
            _suppressionDirty = true;
            if (notifyContext)
            {
                NotifyContextIfChanged(previousTopScope, _topScope);
            }
            else
            {
                QueueContextNotification(previousTopScope);
            }
        }

        private void QueueContextNotification(UXNavigationScope previousTopScope)
        {
            if (!_contextNotificationDirty)
            {
                _pendingPreviousTopScope = previousTopScope;
                _contextNotificationDirty = true;
            }
        }

        private void NotifyContextIfChanged(UXNavigationScope previousTopScope, UXNavigationScope currentTopScope)
        {
            if (_contextNotificationDirty)
            {
                previousTopScope = _pendingPreviousTopScope;
                _pendingPreviousTopScope = null;
                _contextNotificationDirty = false;
            }

            NotifyModeChangeProcessor(UXNavigationModeListener.CurrentMode, previousTopScope, currentTopScope);
        }

        private void NotifyModeChangeProcessor(UXInputMode mode, UXNavigationScope previousTopScope, UXNavigationScope currentTopScope)
        {
            _modeChangeProcessor?.OnNavigationModeChanged(mode, previousTopScope, currentTopScope);
        }

        private static bool IsHigherPriority(UXNavigationScope left, UXNavigationScope right)
        {
            int leftOrder = left.Canvas != null ? left.Canvas.sortingOrder : int.MinValue;
            int rightOrder = right.Canvas != null ? right.Canvas.sortingOrder : int.MinValue;
            if (leftOrder != rightOrder)
            {
                return leftOrder > rightOrder;
            }

            int leftDepth = left.GetHierarchyDepth();
            int rightDepth = right.GetHierarchyDepth();
            if (leftDepth != rightDepth)
            {
                return leftDepth > rightDepth;
            }

            return left.ActivationSerial > right.ActivationSerial;
        }

        private static void ReportCapacityExceeded()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError("UXNavigationRuntime scope capacity exceeded.");
#endif
        }

        private void RegisterLoadedScopes()
        {
            UXNavigationScope[] scopes = FindObjectsOfType<UXNavigationScope>(true);
            for (int i = 0; i < scopes.Length; i++)
            {
                RegisterScope(scopes[i]);
            }
        }
    }
}
#endif
