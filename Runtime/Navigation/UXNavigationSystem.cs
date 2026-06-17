#if INPUTSYSTEM_SUPPORT
using System;
using AlicizaX.UI.Runtime;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace AlicizaX.UI.UXNavigation
{
    internal static class UXNavigationSystem
    {
        private const int ScopeCapacity = 128;
        private const int InvalidIndex = -1;

        private static readonly UXNavigationScope[] _scopes = new UXNavigationScope[ScopeCapacity];
        private static int _scopeCount;

        private static UXNavigationScope _topScope;
        private static ulong _activationSerial;
        private static bool _stateDirty = true;
        private static bool _suppressionDirty = true;
        private static bool _isFlushingState;
        private static bool _initialized;
        private static bool _isShuttingDown;
        private static bool _gamepadRequireSelection = true;
        private static bool _keyboardRequireSelection;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOnLoad()
        {
            _isShuttingDown = false;
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized || _isShuttingDown)
            {
                return;
            }

            UXInput.Watch.Initialize();
            ResetStaticState(false);
            _initialized = true;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
            SubscribeInputWatcher();
            RegisterLoadedScopes();

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        private static void Shutdown()
        {
            if (!_initialized)
            {
                return;
            }

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            Application.quitting -= OnApplicationQuitting;
            UnsubscribeInputWatcher();
            ResetStaticState(true);
        }


        internal static void RequestRefresh(bool ensureSelection)
        {
            if (!EnsureInitialized())
            {
                return;
            }

            MarkStateDirtyInternal();
            FlushStateIfDirty(ensureSelection);
        }

        internal static void RequestEnsureSelection()
        {
            if (!EnsureInitialized())
            {
                return;
            }

            FlushStateIfDirty(true);
        }

        internal static void RegisterScope(UXNavigationScope scope)
        {
            if (!EnsureInitialized())
            {
                return;
            }

            RegisterScopeInternal(scope);
        }

        private static void RegisterScopeInternal(UXNavigationScope scope)
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
            MarkStateDirtyInternal();
        }

        internal static void UnregisterScope(UXNavigationScope scope)
        {
            if (!EnsureInitialized())
            {
                return;
            }

            if (scope == null)
            {
                return;
            }

            int index = scope.RuntimeIndex;
            if (index < 0 || index >= _scopeCount || _scopes[index] != scope)
            {
                scope.RuntimeIndex = InvalidIndex;
                return;
            }

            if (_topScope == scope)
            {
                CaptureTopScopeSelection();
                SetTopScope(null);
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

            MarkStateDirtyInternal();
        }

        internal static void MarkStateDirty()
        {
            if (!EnsureInitialized())
            {
                return;
            }

            MarkStateDirtyInternal();
        }

        private static void MarkStateDirtyInternal()
        {
            _stateDirty = true;
            _suppressionDirty = true;
        }

        internal static void InvalidateSkipCaches()
        {
            if (!EnsureInitialized())
            {
                return;
            }

            for (int i = 0; i < _scopeCount; i++)
            {
                _scopes[i].InvalidateSkipCacheOnly();
            }

            MarkStateDirtyInternal();
        }

        private static bool EnsureInitialized()
        {
            if (_isShuttingDown)
            {
                return false;
            }

            if (!_initialized)
            {
                Initialize();
            }

            return _initialized;
        }

        private static void FlushStateIfDirty(bool ensureSelection = true)
        {
            if (_isFlushingState || (!_stateDirty && !_suppressionDirty && !ensureSelection))
            {
                return;
            }

            _isFlushingState = true;
            CaptureTopScopeSelection();
            if (_stateDirty)
            {
                UXNavigationScope newTopScope = FindTopScope();
                _stateDirty = false;
                SetTopScope(newTopScope);
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
        }

        private static UXNavigationScope FindTopScope()
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

        private static void ApplyScopeSuppression()
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

        private static void EnsureNavigationSelection()
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

        private static void CaptureTopScopeSelection()
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

        private static void OnInputTypeChanged(UXInput.Watch.InputType inputType)
        {
            if (ShouldEnsureSelection())
            {
                FlushStateIfDirty(true);
            }
        }

        private static void OnInputActivity(UXInput.Watch.InputContext context)
        {
            if (RequiresSelectedForInputType(context.InputType))
            {
                FlushStateIfDirty(true);
            }
        }

        private static bool ShouldEnsureSelection()
        {
            UXInput.Watch.InputType inputType = UXInput.Watch.CurrentInputType;
            return RequiresSelectedForInputType(inputType)
                   || (_topScope != null
                       && inputType == UXInput.Watch.InputType.KeyboardMouse
                       && _topScope.HasValidDefaultSelectable());
        }

        private static bool RequiresSelectedForInputType(UXInput.Watch.InputType inputType)
        {
            return ((inputType == UXInput.Watch.InputType.Gamepad || inputType == UXInput.Watch.InputType.Joystick) && _gamepadRequireSelection)
                   || (inputType == UXInput.Watch.InputType.KeyboardMouse && _keyboardRequireSelection);
        }

        private static void SetTopScope(UXNavigationScope topScope)
        {
            if (ReferenceEquals(_topScope, topScope))
            {
                return;
            }

            CaptureTopScopeSelection();
            _topScope = topScope;
            _suppressionDirty = true;
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
            Debug.LogError("UXNavigationSystem scope capacity exceeded.");
#endif
        }

        private static void RegisterLoadedScopes()
        {
            UXNavigationScope[] scopes = UnityEngine.Object.FindObjectsOfType<UXNavigationScope>(true);
            for (int i = 0; i < scopes.Length; i++)
            {
                RegisterScopeInternal(scopes[i]);
            }
        }

        private static void SubscribeInputWatcher()
        {
            UXInput.Watch.OnInputTypeChanged -= OnInputTypeChanged;
            UXInput.Watch.OnInputTypeChanged += OnInputTypeChanged;
            UXInput.Watch.OnInputActivity -= OnInputActivity;
            UXInput.Watch.OnInputActivity += OnInputActivity;
        }

        private static void UnsubscribeInputWatcher()
        {
            UXInput.Watch.OnInputTypeChanged -= OnInputTypeChanged;
            UXInput.Watch.OnInputActivity -= OnInputActivity;
        }

        private static void ResetStaticState(bool clearEventListeners)
        {
            for (int i = 0; i < _scopeCount; i++)
            {
                UXNavigationScope scope = _scopes[i];
                if (scope != null)
                {
                    scope.IsAvailable = false;
                    scope.WasAvailable = false;
                    scope.SetNavigationSuppressed(false);
                    scope.RuntimeIndex = InvalidIndex;
                }
            }

            Array.Clear(_scopes, 0, _scopes.Length);
            _scopeCount = 0;
            _topScope = null;
            _activationSerial = 0;
            _stateDirty = true;
            _suppressionDirty = true;
            _isFlushingState = false;
            _initialized = false;
            _gamepadRequireSelection = true;
            _keyboardRequireSelection = false;
        }

        private static void OnApplicationQuitting()
        {
            _isShuttingDown = true;
            Shutdown();
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                _isShuttingDown = true;
                Shutdown();
            }
        }
#endif
    }
}
#endif
