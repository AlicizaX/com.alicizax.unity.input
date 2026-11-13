#if INPUTSYSTEM_SUPPORT
using System;
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AlicizaX.Input
{
    /// <summary>
    /// 手柄 / 键盘导航焦点维护。
    /// 结构性变化（scope 注册注销、Holder 显示关闭、控件增删）同步全量刷新；
    /// 每帧只做快路径检查，状态没变就不再遍历候选控件与 Canvas 排序。
    /// </summary>
    public sealed class InputNavigation
    {
        private const int ScopeCapacity = 128;
        private const int InvalidIndex = -1;
        private const int MaxRefreshIterations = 8;

        private readonly InputWatch _watch;
        private readonly Action _afterUpdateHandler;
        private NavigationScope[] _scopes = new NavigationScope[ScopeCapacity];
        private int _scopeCount;
        private NavigationScope _topScope;
        private ulong _activationSerial;
        private bool _refreshRequested;
        private bool _isRefreshing;
        private bool _gamepadRequireSelection = true;
        private bool _keyboardRequireSelection;
        private bool _forcedSelection; // 上次刷新时的补选策略；输入类型切换会改变它
        private GameObject _observedSelection;
        private Selectable _observedSelectable;
        private NavigationScope _selectionScope;

        internal InputNavigation(InputWatch watch)
        {
            _watch = watch;
            _afterUpdateHandler = OnAfterUpdate;
        }

        internal int ScopeCount => _scopeCount;

        /// <summary>仅在导航系统同步应用 EventSystem 选择期间为 true；供交互反馈区分程序化选中与用户操作。</summary>
        internal bool IsApplyingSelection { get; private set; }

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
            InputSystem.onAfterUpdate += _afterUpdateHandler;
            Refresh();
        }

        internal void Shutdown()
        {
            InputSystem.onAfterUpdate -= _afterUpdateHandler;
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
            ClearObservedSelection();
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

        /// <summary>立即全量刷新：解析各 scope 的存活、遮挡、候选控件，并应用 EventSystem 选择。</summary>
        internal void Refresh()
        {
            _refreshRequested = true;
            EventSystem eventSystem = EventSystem.current;
            if (_isRefreshing || (eventSystem != null && eventSystem.alreadySelecting))
                return;

            _isRefreshing = true;
            try
            {
                int iterations = 0;
                do
                {
                    if (++iterations > MaxRefreshIterations)
                    {
                        // 选中回调里反复改动导航状态；放弃本轮，下一帧快路径会重新发现变化。
                        Debug.Assert(false, "Navigation refresh did not settle; a selection callback keeps changing navigation state.");
                        _refreshRequested = false;
                        break;
                    }

                    _refreshRequested = false;
                    _forcedSelection = ShouldForceSelection();
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

        private void OnAfterUpdate()
        {
            if (NeedsRefresh()) Refresh();
        }

        /// <summary>
        /// 每帧快路径：只检测无事件通知的变化。稳态成本是一次选中读取、每个 scope 一次存活检查、一次控件可用性检查。
        /// 运行时修改 Canvas sortingOrder、或非当前选中域的控件 interactable 变化不会在这里发现，
        /// 需要时调用 <see cref="NavigationScope.NotifySelectableStateChanged"/>。
        /// </summary>
        private bool NeedsRefresh()
        {
            if (_refreshRequested) return true;
            if (_scopeCount == 0) return false;

            // 父窗口关闭、物体失活等不会通知到本 scope 的存活变化。
            for (int i = 0; i < _scopeCount; i++)
            {
                NavigationScope scope = _scopes[i];
                if (scope.IsAlive != IsScopeAlive(scope)) return true;
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;

            bool force = ShouldForceSelection();
            if (force != _forcedSelection) return true;                       // 键鼠 / 手柄切换
            GameObject selected = eventSystem.currentSelectedGameObject;
            if (!ReferenceEquals(selected, _observedSelection)) return true; // 业务代码或鼠标改了选中
            if (selected == null) return force;                                // 需要补选
            return _selectionScope != null && !NavigationScope.IsSelectableUsable(_observedSelectable); // 当前选中失效
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
                bool allowed = scope.IsAlive
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
                ClearObservedSelection();
                return null;
            }

            if (_observedSelection != selectedObject)
            {
                _observedSelection = selectedObject;
                _selectionScope = selectedObject.GetComponentInParent<NavigationScope>(true);
            }

            _observedSelectable = _selectionScope != null && _selectionScope.RuntimeIndex != InvalidIndex
                ? _selectionScope.GetOwnedSelectable(selectedObject)
                : null;
            // 退出时控件可能已经不可交互；按归属补记，恢复时再检查可用性。
            if (_observedSelectable != null && !_selectionScope.NavigationSuppressed)
                _selectionScope.RecordSelection(_observedSelectable);
            return _observedSelectable;
        }

        private void ClearObservedSelection()
        {
            _observedSelection = null;
            _observedSelectable = null;
            _selectionScope = null;
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
                ClearObservedSelection();
            }
        }

        private bool ShouldForceSelection()
        {
            InputWatch.InputType inputType = _watch.CurrentInputType;
            return (inputType == InputWatch.InputType.Gamepad && _gamepadRequireSelection)
                   || (inputType == InputWatch.InputType.KeyboardMouse && _keyboardRequireSelection);
        }

        /// <summary>
        /// 嵌套 scope 优先于其祖先（与热键的 leaf scope 规则一致），其余按 <see cref="InputLayerOrder"/>：
        /// Canvas 排序 → 显示序号（后显示优先）→ 层级深度。
        /// </summary>
        private static bool IsHigherPriority(NavigationScope left, NavigationScope right)
        {
            Transform leftTransform = left.transform;
            Transform rightTransform = right.transform;
            if (leftTransform.IsChildOf(rightTransform)) return true;
            if (rightTransform.IsChildOf(leftTransform)) return false;

            return InputLayerOrder.IsHigher(
                InputLayerOrder.SortingOrderOf(left.SortingCanvas), left.ActivationSerial, left.GetHierarchyDepth(),
                InputLayerOrder.SortingOrderOf(right.SortingCanvas), right.ActivationSerial, right.GetHierarchyDepth());
        }
    }
}
#endif
