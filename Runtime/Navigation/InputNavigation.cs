#if INPUTSYSTEM_SUPPORT
using System;
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AlicizaX.UI.UXNavigation
{
    public sealed class InputNavigation
    {
        private const int ScopeCapacity = 128;
        private const int InvalidIndex = -1;

        private readonly InputWatch _watch;
        private NavigationScope[] _scopes = new NavigationScope[ScopeCapacity];
        private int _scopeCount;
        private NavigationScope _topScope;
        private ulong _activationSerial;
        private bool _refreshRequested;
        private bool _isRefreshing;
        private bool _gamepadRequireSelection = true;
        private bool _keyboardRequireSelection;
        private GameObject _observedSelection;
        private NavigationScope _selectionScope;

        internal InputNavigation(InputWatch watch) { _watch = watch; }
        internal int ScopeCount => _scopeCount;

        /// <summary>仅在导航系统同步应用 EventSystem 选择期间为 true。</summary>
        public bool IsApplyingSelection { get; private set; }

        /// <summary>手柄/摇杆是否自动补选有效控件。默认 true；关闭策略不清空合法焦点。</summary>
        public bool GamepadRequireSelection
        {
            get => _gamepadRequireSelection;
            set
            {
                if (_gamepadRequireSelection == value) return;
                _gamepadRequireSelection = value;
                Refresh();
            }
        }

        /// <summary>键鼠是否自动补选有效控件。默认 false；失效或被遮挡的焦点仍会清理。</summary>
        public bool KeyboardRequireSelection
        {
            get => _keyboardRequireSelection;
            set
            {
                if (_keyboardRequireSelection == value) return;
                _keyboardRequireSelection = value;
                Refresh();
            }
        }

        public void SetRequireSelection(bool gamepad, bool keyboard)
        {
            if (_gamepadRequireSelection == gamepad && _keyboardRequireSelection == keyboard) return;
            _gamepadRequireSelection = gamepad;
            _keyboardRequireSelection = keyboard;
            Refresh();
        }

        internal void Initialize()
        {
            // 与输入生命周期配对，InputService 禁用也不会停止导航维护。
            InputSystem.onAfterUpdate += Refresh;
            Refresh();
        }

        internal void Shutdown()
        {
            InputSystem.onAfterUpdate -= Refresh;
            CaptureSelection();
            for (int i = 0; i < _scopeCount; i++)
            {
                NavigationScope scope = _scopes[i];
                _scopes[i] = null;
                if (scope.IsNavigationAllowed) scope.EndNavigation();
                scope.IsAlive = false;
                scope.IsNavigationAllowed = false;
                scope.RuntimeIndex = InvalidIndex;
                scope.SetNavigationSuppressed(false);
            }

            _topScope = null;
            _scopeCount = 0;
            _observedSelection = null;
            _selectionScope = null;
            _refreshRequested = false;
        }

        internal void RegisterScope(NavigationScope scope)
        {
            if (scope.RuntimeIndex != InvalidIndex) return;
            if (_scopeCount == _scopes.Length)
                Array.Resize(ref _scopes, _scopes.Length * 2);

            int index = _scopeCount++;
            _scopes[index] = scope;
            scope.RuntimeIndex = index;
            _observedSelection = null;
            Refresh();
        }

        internal void UnregisterScope(NavigationScope scope)
        {
            int index = scope.RuntimeIndex;
            if (index == InvalidIndex) return;

            CaptureSelection();
            if (scope.IsNavigationAllowed) scope.EndNavigation();
            scope.IsAlive = false;
            scope.IsNavigationAllowed = false;
            scope.RuntimeIndex = InvalidIndex;
            if (_topScope == scope) _topScope = null;

            int last = --_scopeCount;
            NavigationScope movedScope = _scopes[last];
            _scopes[last] = null;
            if (index != last)
            {
                _scopes[index] = movedScope;
                movedScope.RuntimeIndex = index;
            }

            scope.SetNavigationSuppressed(false);
            Refresh();
        }

        internal void Refresh()
        {
            _refreshRequested = true;
            EventSystem eventSystem = EventSystem.current;
            if (_isRefreshing || (eventSystem != null && eventSystem.alreadySelecting))
                return;

            _isRefreshing = true;
            try
            {
                do
                {
                    _refreshRequested = false;
                    eventSystem = EventSystem.current;
                    GameObject currentSelected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
                    Selectable selection = ObserveSelection(currentSelected);
                    NavigationScope nextScope = ResolveScopes(out Selectable preferred);
                    if (_refreshRequested) continue;
                    bool scopeChanged = _topScope != nextScope;
                    _topScope = nextScope;
                    ApplySelection(eventSystem, scopeChanged, currentSelected, selection, preferred);
                } while (_refreshRequested);
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private NavigationScope ResolveScopes(out Selectable preferred)
        {
            preferred = null;
            NavigationScope highestOccluder = null;
            for (int i = 0; i < _scopeCount; i++)
            {
                NavigationScope scope = _scopes[i];
                bool alive = IsScopeAlive(scope);
                if (scope.IsAlive != alive)
                {
                    scope.IsAlive = alive;
                    if (alive) scope.ActivationSerial = ++_activationSerial;
                }

                if (alive && scope.BlockLowerScopes && (highestOccluder == null || IsHigherPriority(scope, highestOccluder)))
                    highestOccluder = scope;
            }

            NavigationScope bestScope = null;
            for (int i = 0; i < _scopeCount; i++)
            {
                NavigationScope scope = _scopes[i];
                bool allowed = scope.IsAlive && scope.Navigable
                               && (highestOccluder == null || highestOccluder == scope
                                   || !IsHigherPriority(highestOccluder, scope));
                if (scope.IsNavigationAllowed && !allowed) scope.EndNavigation();
                scope.IsNavigationAllowed = allowed;

                Selectable candidate = allowed ? scope.GetPreferredSelectable() : null;
                scope.SetNavigationSuppressed(candidate == null);
                if (_refreshRequested) return null;
                if (candidate != null && (bestScope == null || IsHigherPriority(scope, bestScope)))
                {
                    bestScope = scope;
                    preferred = candidate;
                }
            }

            return bestScope;
        }

        private static bool IsScopeAlive(NavigationScope scope)
        {
            UIHolderObjectBase holder = scope.Holder;
            return scope.isActiveAndEnabled && holder != null && holder.IsEnabled;
        }

        private void CaptureSelection()
        {
            EventSystem eventSystem = EventSystem.current;
            ObserveSelection(eventSystem != null ? eventSystem.currentSelectedGameObject : null);
        }

        private Selectable ObserveSelection(GameObject selectedObject)
        {
            if (selectedObject == null)
            {
                _observedSelection = null;
                _selectionScope = null;
                return null;
            }

            if (_observedSelection != selectedObject)
            {
                _observedSelection = selectedObject;
                _selectionScope = selectedObject.GetComponentInParent<NavigationScope>(true);
            }

            if (_selectionScope == null || _selectionScope.RuntimeIndex == InvalidIndex) return null;
            Selectable selected = _selectionScope.GetOwnedSelectable(selectedObject);
            // 退出时控件可能已经不可交互；按归属补记，恢复时再检查可用性。
            if (selected != null && !_selectionScope.NavigationSuppressed)
                _selectionScope.RecordSelection(selected);
            return selected;
        }

        private void ApplySelection(EventSystem eventSystem, bool scopeChanged,
            GameObject currentSelected, Selectable selection, Selectable preferred)
        {
            if (eventSystem == null) return;

            bool requireSelection = ShouldForceSelection();
            if (currentSelected != null)
            {
                bool usable = _selectionScope != null
                              && _selectionScope.RuntimeIndex != InvalidIndex
                              && !_selectionScope.NavigationSuppressed
                              && NavigationScope.IsSelectableUsable(selection);
                if (usable && (!scopeChanged || !requireSelection || _selectionScope == _topScope))
                    return;

                // 未归属导航域的键鼠选中对象由其原有 UI 逻辑管理。
                if (_selectionScope == null && !requireSelection) return;
                // 非阻挡上层实际接管焦点时，才结束原焦点域。
                if (usable) _selectionScope.EndNavigation();
                SetSelected(eventSystem, null);
                if (_refreshRequested) return;
                if (_topScope != null && !NavigationScope.IsSelectableUsable(preferred))
                {
                    _refreshRequested = true;
                    return;
                }
            }

            if (!requireSelection || _topScope == null) return;

            if (preferred != null) SetSelected(eventSystem, preferred.gameObject);
        }

        private void SetSelected(EventSystem eventSystem, GameObject selected)
        {
            IsApplyingSelection = true;
            try
            {
                eventSystem.SetSelectedGameObject(selected);
            }
            finally
            {
                IsApplyingSelection = false;
                _observedSelection = null;
                _selectionScope = null;
            }
        }

        private bool ShouldForceSelection()
        {
            InputWatch.InputType inputType = _watch.CurrentInputType;
            return (inputType == InputWatch.InputType.Gamepad && _gamepadRequireSelection)
                   || (inputType == InputWatch.InputType.KeyboardMouse && _keyboardRequireSelection);
        }

        private static bool IsHigherPriority(NavigationScope left, NavigationScope right)
        {
            int leftOrder = left.SortingCanvas.sortingOrder;
            int rightOrder = right.SortingCanvas.sortingOrder;
            if (leftOrder != rightOrder) return leftOrder > rightOrder;

            int leftDepth = left.GetHierarchyDepth();
            int rightDepth = right.GetHierarchyDepth();
            if (leftDepth != rightDepth) return leftDepth > rightDepth;

            return left.ActivationSerial > right.ActivationSerial;
        }
    }
}
#endif
