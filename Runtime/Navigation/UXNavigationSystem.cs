#if INPUTSYSTEM_SUPPORT
using AlicizaX;
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlicizaX.UI.UXNavigation
{
    public static class UXNavigationSystem
    {
        private const int ScopeCapacity = 128;
        private const int InvalidIndex = -1;

        private static readonly UXNavigationScope[] _scopes = new UXNavigationScope[ScopeCapacity];
        private static int _scopeCount;

        private static UXNavigationScope _topScope;
        private static ulong _activationSerial;
        private static bool _dirty = true;
        private static bool _isFlushingState;
        private static bool _initialized;
        private static bool _gamepadRequireSelection = true;
        private static bool _keyboardRequireSelection;

        /// <summary>
        /// 手柄/摇杆是否强制至少选中一个可交互控件。默认 true。
        /// 仅“补选”，不会因为策略关闭而清空已有焦点。
        /// </summary>
        public static bool GamepadRequireSelection
        {
            get => _gamepadRequireSelection;
            set
            {
                if (_gamepadRequireSelection == value)
                {
                    return;
                }

                _gamepadRequireSelection = value;
                OnRequireSelectionPolicyChanged();
            }
        }

        /// <summary>
        /// 键鼠是否强制至少选中一个可交互控件。默认 false。
        /// 仅“补选”，不会因为策略关闭而清空已有焦点。
        /// </summary>
        public static bool KeyboardRequireSelection
        {
            get => _keyboardRequireSelection;
            set
            {
                if (_keyboardRequireSelection == value)
                {
                    return;
                }

                _keyboardRequireSelection = value;
                OnRequireSelectionPolicyChanged();
            }
        }

        /// <summary>
        /// 一次性配置手柄与键鼠的强制选中策略。
        /// </summary>
        public static void SetRequireSelection(bool gamepad, bool keyboard)
        {
            bool changed = _gamepadRequireSelection != gamepad || _keyboardRequireSelection != keyboard;
            _gamepadRequireSelection = gamepad;
            _keyboardRequireSelection = keyboard;
            if (changed)
            {
                OnRequireSelectionPolicyChanged();
            }
        }

        internal static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            SubscribeInputWatcher();
            FlushStateIfDirty(ShouldEnsureSelection());
        }

        internal static void Shutdown()
        {
            UnsubscribeInputWatcher();
            CaptureTopScopeSelection();
            for (int i = 0; i < _scopeCount; i++)
            {
                UXNavigationScope scope = _scopes[i];
                if (scope == null)
                {
                    continue;
                }

                scope.IsAlive = false;
                scope.IsAvailable = false;
                scope.WasAlive = false;
                scope.SetNavigationSuppressed(false);
            }

            _topScope = null;
            _dirty = true;
            _isFlushingState = false;
            _initialized = false;
        }

        internal static void RequestRefresh(bool ensureSelection)
        {
            _dirty = true;
            FlushStateIfDirty(ensureSelection);
        }

        internal static void RegisterScope(UXNavigationScope scope)
        {
            if (scope == null || scope.RuntimeIndex != InvalidIndex)
            {
                return;
            }

            if (_scopeCount >= _scopes.Length)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Log.Error("UXNavigationSystem scope capacity exceeded.");
#endif
                return;
            }

            int index = _scopeCount++;
            _scopes[index] = scope;
            scope.RuntimeIndex = index;
            _dirty = true;
            FlushStateIfDirty(false);
        }

        internal static void UnregisterScope(UXNavigationScope scope)
        {
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
                _topScope = null;
            }

            scope.IsAlive = false;
            scope.IsAvailable = false;
            scope.WasAlive = false;
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

            _dirty = true;
            FlushStateIfDirty(false);
        }

        internal static void MarkStateDirty()
        {
            _dirty = true;
            FlushStateIfDirty(false);
        }

        private static void FlushStateIfDirty(bool ensureSelection)
        {
            if (!_initialized || _isFlushingState || (!_dirty && !ensureSelection))
            {
                return;
            }

            _isFlushingState = true;
            CaptureTopScopeSelection();

            UXNavigationScope highestOccluder;
            if (_dirty)
            {
                UXNavigationScope newTopScope = ResolveScopes(out highestOccluder);
                _dirty = false;
                if (!ReferenceEquals(_topScope, newTopScope))
                {
                    _topScope = newTopScope;
                }
            }
            else
            {
                highestOccluder = FindHighestOccluder();
            }

            ApplyScopeSuppression(highestOccluder);

            if (ensureSelection && ShouldEnsureSelection())
            {
                EnsureNavigationSelection();
            }

            _isFlushingState = false;
        }

        private static UXNavigationScope ResolveScopes(out UXNavigationScope highestOccluder)
        {
            UXNavigationScope bestScope = null;
            highestOccluder = null;
            for (int i = 0; i < _scopeCount; i++)
            {
                UXNavigationScope scope = _scopes[i];
                bool alive = IsScopeAlive(scope);
                scope.IsAlive = alive;
                if (scope.WasAlive != alive)
                {
                    scope.WasAlive = alive;
                    if (alive)
                    {
                        scope.ActivationSerial = ++_activationSerial;
                    }
                }

                bool focusable = alive && scope.Navigable && scope.HasAvailableSelectable();
                scope.IsAvailable = focusable;

                if (alive && scope.BlockLowerScopes && (highestOccluder == null || IsHigherPriority(scope, highestOccluder)))
                {
                    highestOccluder = scope;
                }

                if (focusable && (bestScope == null || IsHigherPriority(scope, bestScope)))
                {
                    bestScope = scope;
                }
            }

            if (highestOccluder != null && !highestOccluder.IsAvailable)
            {
                return null;
            }

            return bestScope;
        }

        private static UXNavigationScope FindHighestOccluder()
        {
            UXNavigationScope highestOccluder = null;
            for (int i = 0; i < _scopeCount; i++)
            {
                UXNavigationScope scope = _scopes[i];
                if (scope.IsAlive && scope.BlockLowerScopes && (highestOccluder == null || IsHigherPriority(scope, highestOccluder)))
                {
                    highestOccluder = scope;
                }
            }

            return highestOccluder;
        }

        private static bool IsScopeAlive(UXNavigationScope scope)
        {
            if (scope == null || !scope.isActiveAndEnabled || !scope.gameObject.activeInHierarchy)
            {
                return false;
            }

            Canvas canvas = scope.Canvas;
            if (canvas == null || !canvas.enabled)
            {
                return false;
            }

            UIHolderObjectBase holder = scope.Holder;
            int layer = holder != null ? holder.gameObject.layer : canvas.gameObject.layer;
            return layer == UIComponent.UIShowLayer;
        }

        private static void ApplyScopeSuppression(UXNavigationScope highestOccluder)
        {
            for (int i = 0; i < _scopeCount; i++)
            {
                UXNavigationScope scope = _scopes[i];
                bool suppress = !scope.IsAlive
                                || !scope.IsAvailable
                                || (highestOccluder != null
                                    && highestOccluder != scope
                                    && IsHigherPriority(highestOccluder, scope));
                scope.SetNavigationSuppressed(suppress);
            }
        }

        private static void EnsureNavigationSelection()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }

            if (_topScope == null)
            {
                if (eventSystem.currentSelectedGameObject != null)
                {
                    SetSelected(eventSystem, null);
                }

                return;
            }

            GameObject currentSelected = eventSystem.currentSelectedGameObject;
            if (_topScope.IsSelectableOwnedAndValid(currentSelected))
            {
                _topScope.RecordSelection(currentSelected);
                return;
            }

            Selectable preferred = _topScope.GetPreferredSelectable();
            if (preferred == null)
            {
                return;
            }

            SetSelected(eventSystem, preferred.gameObject);
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

        private static void SetSelected(EventSystem eventSystem, GameObject selected)
        {
            using (new UXFocusChange.Scope(UXFocusChange.Cause.Programmatic))
            {
                eventSystem.SetSelectedGameObject(selected);
            }
        }

        private static void OnInputTypeChanged(UXInput.Watch.InputType _)
        {
            FlushStateIfDirty(ShouldEnsureSelection());
        }

        private static void OnRequireSelectionPolicyChanged()
        {
            FlushStateIfDirty(ShouldEnsureSelection());
        }

        private static bool ShouldEnsureSelection()
        {
            UXInput.Watch.InputType inputType = UXInput.Watch.CurrentInputType;
            return ((inputType == UXInput.Watch.InputType.Gamepad || inputType == UXInput.Watch.InputType.Joystick) && _gamepadRequireSelection)
                   || (inputType == UXInput.Watch.InputType.KeyboardMouse && _keyboardRequireSelection);
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

        private static void SubscribeInputWatcher()
        {
            UXInput.Watch.OnInputTypeChanged -= OnInputTypeChanged;
            UXInput.Watch.OnInputTypeChanged += OnInputTypeChanged;
        }

        private static void UnsubscribeInputWatcher()
        {
            UXInput.Watch.OnInputTypeChanged -= OnInputTypeChanged;
        }
    }
}
#endif
