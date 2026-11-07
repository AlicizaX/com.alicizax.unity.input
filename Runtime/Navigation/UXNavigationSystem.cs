#if INPUTSYSTEM_SUPPORT
using System;
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

        private static UXNavigationScope[] _scopes = new UXNavigationScope[ScopeCapacity];
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
            _initialized = true;
            SubscribeInputWatcher();
            RefreshNavigation(ShouldForceSelection());
        }

        internal static void Shutdown()
        {
            _initialized = false;
            UnsubscribeInputWatcher();
            CaptureTopScopeSelection();
            for (int i = 0; i < _scopeCount; i++)
            {
                UXNavigationScope scope = _scopes[i];
                _scopes[i] = null;
                if (scope == null)
                {
                    continue;
                }

                scope.IsAlive = false;
                scope.IsAvailable = false;
                scope.WasAlive = false;
                scope.RuntimeIndex = InvalidIndex;
                scope.ActivationSerial = 0;
                scope.SetNavigationSuppressed(false);
            }

            _topScope = null;
            _scopeCount = 0;
            _activationSerial = 0;
            _gamepadRequireSelection = true;
            _keyboardRequireSelection = false;
            UXFocusChange.Reset();
            _dirty = true;
            _isFlushingState = false;
        }

        internal static void RequestRefresh(bool ensureSelection)
        {
            _dirty = true;
            RefreshNavigation(ensureSelection);
        }

        internal static void RegisterScope(UXNavigationScope scope)
        {
            if (scope == null || scope.RuntimeIndex != InvalidIndex)
            {
                return;
            }

            if (_scopeCount == _scopes.Length)
            {
                Array.Resize(ref _scopes, _scopes.Length * 2);
            }

            int index = _scopeCount++;
            _scopes[index] = scope;
            scope.RuntimeIndex = index;
            _dirty = true;
            RefreshNavigation(false);
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
            if (index != last && movedScope != null)
            {
                _scopes[index] = movedScope;
                movedScope.RuntimeIndex = index;
            }
            else if (index != last)
            {
                _scopes[index] = null;
            }

            _dirty = true;
            RefreshNavigation(false);
        }

        internal static void InvalidateNavigation()
        {
            _dirty = true;
            RefreshNavigation(false);
        }

        private static void RefreshNavigation(bool ensureSelection)
        {
            if (!_initialized || _isFlushingState || (!_dirty && !ensureSelection))
            {
                return;
            }

            _isFlushingState = true;
            try
            {
                do
                {
                    CaptureTopScopeSelection();
                    _dirty = false;
                    _topScope = ResolveScopes(out UXNavigationScope highestOccluder);
                    ApplyScopeSuppression(highestOccluder);

                    if (ensureSelection && ShouldForceSelection())
                    {
                        ApplyDefaultSelection();
                    }
                } while (_dirty && _initialized);
            }
            finally
            {
                _isFlushingState = false;
            }
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

            if (highestOccluder != null && !highestOccluder.IsAvailable &&
                (bestScope == null || IsHigherPriority(highestOccluder, bestScope)))
            {
                return null;
            }

            return bestScope;
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

        private static void ApplyDefaultSelection()
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
            if (_topScope.IsOwnedAndUsable(currentSelected))
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
            if (_topScope.IsOwnedAndUsable(selectedObject))
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

        private static void OnInputTypeChanged(InputWatch.InputType _)
        {
            RefreshNavigation(ShouldForceSelection());
        }

        private static void OnRequireSelectionPolicyChanged()
        {
            RefreshNavigation(ShouldForceSelection());
        }

        private static bool ShouldForceSelection()
        {
            if (!_initialized) return false;
            InputWatch.InputType inputType = UXInput.Watch.CurrentInputType;
            return ((inputType == InputWatch.InputType.Gamepad || inputType == InputWatch.InputType.Joystick) && _gamepadRequireSelection)
                   || (inputType == InputWatch.InputType.KeyboardMouse && _keyboardRequireSelection);
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
            UXInput.Watch.OnInputTypeChanged += OnInputTypeChanged;
        }

        private static void UnsubscribeInputWatcher()
        {
            UXInput.Watch.OnInputTypeChanged -= OnInputTypeChanged;
        }
    }
}
#endif
