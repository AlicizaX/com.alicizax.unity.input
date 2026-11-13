#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    public enum EHotkeyPressType : byte
    {
        Started = 0,
        Performed = 1,
        Canceled = 2
    }

    /// <summary>组件持有的注册令牌：只由 InputHotkey 的注册/注销路径写入。</summary>
    internal readonly struct HotkeyRegistrationToken
    {
        public readonly HotkeyScope Scope;
        public readonly InputAction Action;
        public readonly EHotkeyPressType PressType;

        public HotkeyRegistrationToken(HotkeyScope scope, InputAction action, EHotkeyPressType pressType)
        {
            Scope = scope;
            Action = action;
            PressType = pressType;
        }

        public bool IsRegistered => Action != null;
    }

    /// <summary>
    /// 热键模块：把 <see cref="HotkeyComponentBase"/> 注册到所属 Holder 的作用域，按当前焦点链分发按键。
    /// 实例由 <see cref="UXInput"/> 创建并统一启停，模块内部不保存静态状态。
    /// 同一 Holder/action/pressType 允许存在多个注册，后注册（后 SetActive/OnEnable）者优先；
    /// 不同 Holder 的 scope 按 Canvas 排序、显示序号、层级深度选择。
    /// </summary>
    public sealed class InputHotkey
    {
        private sealed class HotkeyActionState
        {
            public int Started;
            public int Performed;
            public int Canceled;
            public UIHolderObjectBase FocusHolder;
            public HotkeyScope LeafScope;

            public int Total => Started + Performed + Canceled;

            public void AdjustCount(EHotkeyPressType pressType, int delta)
            {
                switch (pressType)
                {
                    case EHotkeyPressType.Started:
                        Started += delta;
                        break;
                    case EHotkeyPressType.Canceled:
                        Canceled += delta;
                        break;
                    default:
                        Performed += delta;
                        break;
                }
            }

            public void ClearPressTarget()
            {
                FocusHolder = null;
                LeafScope = null;
            }
        }

        private static readonly Predicate<UIHolderObjectBase> HotkeyFocusPredicate = IsHotkeyFocusHolder;

        private readonly Dictionary<InputAction, HotkeyActionState> _actionStates =
            new Dictionary<InputAction, HotkeyActionState>();

        private readonly Dictionary<UIHolderObjectBase, HotkeyScope> _scopesByHolder =
            new Dictionary<UIHolderObjectBase, HotkeyScope>(
                ReferenceEqualityComparer<UIHolderObjectBase>.Instance);

        private readonly List<HotkeyScope> _scopeList = new List<HotkeyScope>(32);

        // Shutdown/DestroyScope 共用；这两条路径内的 Unregister 不得回调业务代码，否则会重入覆盖。
        private readonly List<HotkeyComponentBase> _triggerBuffer = new List<HotkeyComponentBase>(16);
        private readonly Action<InputAction.CallbackContext> _startedHandler;
        private readonly Action<InputAction.CallbackContext> _performedHandler;
        private readonly Action<InputAction.CallbackContext> _canceledHandler;

        /// <summary>关闭时置空：之后的注册解析不到动作而直接失败，无需额外的关闭标志。</summary>
        private InputReader _reader;
        private ulong _activationSerial;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly HashSet<InputAction> _warnedNonButtonActions = new HashSet<InputAction>();
#endif

        internal InputHotkey(InputReader reader)
        {
            _reader = reader;

            // 每个实例只构造一次回调委托，按键分发路径不再产生委托分配。
            _startedHandler = OnActionStarted;
            _performedHandler = OnActionPerformed;
            _canceledHandler = OnActionCanceled;
        }

        /// <summary>
        /// 清空全部热键注册和作用域，不改变动作的启用状态。
        /// 由 <see cref="UXInput"/> 统一调用；关闭后实例不再接受新的注册。
        /// </summary>
        internal void Shutdown()
        {
            CollectRegisteredTriggers(_triggerBuffer);

            for (int i = 0; i < _triggerBuffer.Count; i++)
                UnregisterHotkey(_triggerBuffer[i]);

            _triggerBuffer.Clear();

            // 正常路径最后一个注册移除时已经 Detach 每个 scope、清空 action state；
            // 会话销毁只断言并释放容器，不再做“下游再清一次”的兜底。
            Debug.Assert(_scopeList.Count == 0, "Hotkey shutdown must release every scope.");
            Debug.Assert(_actionStates.Count == 0, "Hotkey shutdown must leave no action state.");

            _scopeList.Clear();
            _scopesByHolder.Clear();
            _actionStates.Clear();
            _reader = null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _warnedNonButtonActions.Clear();
#endif
        }

        /// <summary>
        /// 丢弃当前按下目标，避免恢复后把按键派发到失焦前的窗口。
        /// 由 <see cref="InputService"/> 在应用失焦或暂停时调用。
        /// </summary>
        internal void ResetPressTargets()
        {
            foreach (HotkeyActionState state in _actionStates.Values)
                state.ClearPressTarget();
        }

        /// <summary>
        /// 注册/替换一个 trigger。返回 false 时旧注册保持不变，调用方可以回滚配置字段。
        /// 同一 Holder/action/pressType 的多个 trigger 按注册顺序排序，后注册者优先。
        /// 替换不会拆掉 scope：scope 的显示序号与 action 订阅都保持不变。
        /// </summary>
        internal bool RegisterHotkey(
            HotkeyComponentBase trigger,
            UIHolderObjectBase holder,
            InputActionReference action,
            EHotkeyPressType pressType)
        {
            InputActionAsset asset = _reader != null ? _reader.Actions : null;
            InputAction referenced = action != null ? action.action : null;
            InputAction inputAction = referenced != null && asset != null
                ? asset.FindAction(referenced.id)
                : null;

            if (inputAction == null || holder == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (inputAction == null && action != null && asset != null)
                {
                    Debug.LogWarning(
                        "Hotkey action reference could not be resolved in the active InputActionAsset.",
                        trigger);
                }
#endif
                return false;
            }

            if (pressType > EHotkeyPressType.Canceled)
            {
                Debug.Assert(false, "Invalid hotkey press type.");
                return false;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (inputAction.type != InputActionType.Button && _warnedNonButtonActions.Add(inputAction))
            {
                Debug.LogWarning(
                    inputAction.type == InputActionType.PassThrough
                        ? "Hotkey action '" + inputAction.name + "' is PassThrough. PassThrough actions have no Started phase and are not supported as hotkeys."
                        : "Hotkey action '" + inputAction.name + "' is " + inputAction.type +
                          ". Hotkeys expect Button actions; value actions dispatch on every value change.",
                    trigger);
            }
#endif

            // PassThrough 只有 Performed、没有 Started；按下目标锁不成立，直接拒绝而不是一次按键触发两次。
            if (inputAction.type == InputActionType.PassThrough)
                return false;

            Debug.Assert(trigger.isActiveAndEnabled, "A hotkey must be registered while its component is active.");

            // 先摘掉旧 binding（不 Detach），再加新的：重注册的 trigger 排到最后（后注册者优先），
            // 同一 Holder 的 scope 原样复用。
            HotkeyRegistrationToken previous = trigger.Registration;
            if (previous.IsRegistered && !previous.Scope.RemoveBinding(previous.Action, previous.PressType, trigger))
            {
                Debug.Assert(false, "Hotkey registration token did not match its scope.");
                previous = default;
            }

            HotkeyScope scope = GetOrCreateScope(holder);
            scope.RefreshHierarchy();
            scope.AddBinding(inputAction, pressType, trigger);

            // 先加后减：同一 action 的计数不会掉到 0，不会退订再订阅。
            AddRegistration(inputAction, pressType);
            if (previous.IsRegistered)
            {
                RemoveRegistration(previous.Action, previous.PressType);
                if (previous.Scope.IsEmpty)
                    DetachScope(previous.Scope);
            }

            // 注册只给“尚未有显示序”的 scope 补一次；后续注册不能抢走已打开 widget 的优先级。
            if (IsHolderAvailable(holder) && scope.ActivationSerial == 0)
                scope.ActivationSerial = ++_activationSerial;

            trigger.Registration = new HotkeyRegistrationToken(scope, inputAction, pressType);
            return true;
        }

        internal void UnregisterHotkey(HotkeyComponentBase trigger)
        {
            HotkeyRegistrationToken registration = trigger.Registration;
            if (!registration.IsRegistered)
                return;

            if (!registration.Scope.RemoveBinding(registration.Action, registration.PressType, trigger))
            {
                Debug.Assert(false, "Hotkey registration token did not match its scope.");
                trigger.Registration = default;
                return;
            }

            trigger.Registration = default;

            RemoveRegistration(registration.Action, registration.PressType);
            if (registration.Scope.IsEmpty)
                DetachScope(registration.Scope);
        }

        internal void ActivateScope(HotkeyScope scope)
        {
            scope.RefreshHierarchy();
            scope.ActivationSerial = ++_activationSerial;
        }

        internal void RemovePressTargetsForHolder(UIHolderObjectBase holder)
        {
            foreach (HotkeyActionState state in _actionStates.Values)
            {
                if (InputHierarchyUtility.IsDescendantOrSelf(state.FocusHolder, holder)
                    || state.LeafScope != null && state.LeafScope.IsInside(holder))
                {
                    state.ClearPressTarget();
                }
            }
        }

        internal void DestroyScope(UIHolderObjectBase holder)
        {
            if (!_scopesByHolder.TryGetValue(holder, out HotkeyScope scope))
                return;

            RemovePressTargetsForHolder(holder);

            _triggerBuffer.Clear();
            scope.AppendTriggers(_triggerBuffer);

            for (int i = 0; i < _triggerBuffer.Count; i++)
                UnregisterHotkey(_triggerBuffer[i]);

            _triggerBuffer.Clear();

            Debug.Assert(!_scopesByHolder.ContainsKey(holder),
                "DestroyScope must release every hotkey registration.");
        }

        private void OnActionStarted(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Started);

        private void OnActionPerformed(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Performed);

        private void OnActionCanceled(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Canceled);

        private HotkeyScope GetOrCreateScope(UIHolderObjectBase holder)
        {
            if (_scopesByHolder.TryGetValue(holder, out HotkeyScope scope))
                return scope;

            scope = new HotkeyScope(this, holder);

            holder.OnShowEvent += scope.HandleHolderShow;
            holder.OnCloseEvent += scope.HandleHolderClose;
            holder.OnDestroyEvent += scope.HandleHolderDestroy;

            _scopeList.Add(scope);
            _scopesByHolder.Add(holder, scope);
            return scope;
        }

        private void DetachScope(HotkeyScope scope)
        {
            UIHolderObjectBase holder = scope.Holder;
            if (!ReferenceEquals(holder, null))
            {
                holder.OnShowEvent -= scope.HandleHolderShow;
                holder.OnCloseEvent -= scope.HandleHolderClose;
                holder.OnDestroyEvent -= scope.HandleHolderDestroy;
                _scopesByHolder.Remove(holder);
            }

            _scopeList.Remove(scope);
        }

        private void AddRegistration(InputAction action, EHotkeyPressType pressType)
        {
            if (!_actionStates.TryGetValue(action, out HotkeyActionState state))
            {
                state = new HotkeyActionState();
                _actionStates.Add(action, state);
            }

            if (state.Total == 0)
            {
                SetSubscribed(action, EHotkeyPressType.Started, true);
                SetSubscribed(action, EHotkeyPressType.Canceled, true);
            }

            if (pressType == EHotkeyPressType.Performed && state.Performed == 0)
                SetSubscribed(action, EHotkeyPressType.Performed, true);

            state.AdjustCount(pressType, 1);
        }

        private void RemoveRegistration(InputAction action, EHotkeyPressType pressType)
        {
            if (!_actionStates.TryGetValue(action, out HotkeyActionState state))
            {
                Debug.Assert(false, "Unregistering an action without registration state.");
                return;
            }

            Debug.Assert(state.Total > 0, "Hotkey registration counts must stay non-negative.");
            state.AdjustCount(pressType, -1);

            if (pressType == EHotkeyPressType.Performed && state.Performed == 0)
                SetSubscribed(action, EHotkeyPressType.Performed, false);

            if (state.Total == 0)
            {
                SetSubscribed(action, EHotkeyPressType.Started, false);
                SetSubscribed(action, EHotkeyPressType.Canceled, false);
                _actionStates.Remove(action);
            }
        }

        private void SetSubscribed(InputAction action, EHotkeyPressType pressType, bool subscribed)
        {
            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    if (subscribed)
                        action.started += _startedHandler;
                    else
                        action.started -= _startedHandler;
                    break;
                case EHotkeyPressType.Canceled:
                    if (subscribed)
                        action.canceled += _canceledHandler;
                    else
                        action.canceled -= _canceledHandler;
                    break;
                default:
                    if (subscribed)
                        action.performed += _performedHandler;
                    else
                        action.performed -= _performedHandler;
                    break;
            }
        }

        private void Dispatch(InputAction.CallbackContext context, EHotkeyPressType pressType)
        {
            InputAction action = context.action;
            if (!_actionStates.TryGetValue(action, out HotkeyActionState state))
                return;

            if (pressType == EHotkeyPressType.Started)
            {
                CapturePressTarget(state);
                if (state.Started > 0)
                    DispatchToPressTarget(state, action, pressType);
                return;
            }

            if (pressType == EHotkeyPressType.Canceled)
            {
                if (state.Canceled > 0)
                    DispatchToPressTarget(state, action, pressType);
                state.ClearPressTarget();
                return;
            }

            // Performed 只使用 Started 捕获的目标；按下时没有目标就丢弃整次按键，不再改投当前窗口。
            DispatchToPressTarget(state, action, pressType);
        }

        private void CapturePressTarget(HotkeyActionState state)
        {
            if (!TryGetCurrentHotkeyFocusHolder(out UIHolderObjectBase focusHolder))
            {
                state.ClearPressTarget();
                return;
            }

            state.FocusHolder = focusHolder;
            state.LeafScope = FindLeafScope(focusHolder);
        }

        private static bool TryGetCurrentHotkeyFocusHolder(out UIHolderObjectBase holder)
        {
            holder = null;
            return AppServices.TryGet(out IUIService uiService)
                   && uiService.TryGetTopVisibleHolder(HotkeyFocusPredicate, out holder);
        }

        private static bool IsHotkeyFocusHolder(UIHolderObjectBase holder)
        {
            return holder != null && !holder.TryGetComponent<HotkeyPassThrough>(out _);
        }

        private void DispatchToPressTarget(
            HotkeyActionState state,
            InputAction action,
            EHotkeyPressType pressType)
        {
            UIHolderObjectBase focusHolder = state.FocusHolder;
            HotkeyScope leafScope = state.LeafScope;
            if (focusHolder == null || leafScope == null)
                return;

            if (!IsHolderAvailable(focusHolder) || !leafScope.IsInside(focusHolder))
                return;

            DispatchToScopeChain(leafScope, focusHolder, action, pressType);
        }

        private void DispatchToScopeChain(
            HotkeyScope leafScope,
            UIHolderObjectBase stopHolder,
            InputAction action,
            EHotkeyPressType pressType)
        {
            ReadOnlySpan<UIHolderObjectBase> chain = leafScope.HolderChain;

            for (int i = 0; i < chain.Length; i++)
            {
                UIHolderObjectBase ancestor = chain[i];

                if (_scopesByHolder.TryGetValue(ancestor, out HotkeyScope scope)
                    && IsHolderAvailable(scope.Holder)
                    && scope.TryTrigger(action, pressType))
                {
                    return;
                }

                if (ReferenceEquals(ancestor, stopHolder))
                    return;
            }

            Debug.Assert(false, "The press target holder must be in the scope holder chain.");
        }

        private HotkeyScope FindLeafScope(UIHolderObjectBase focusHolder)
        {
            // 可见窗口状态可能滞后于 activeInHierarchy；这里必须再确认一次。
            if (!IsHolderAvailable(focusHolder))
                return null;

            HotkeyScope bestScope = null;
            for (int i = 0; i < _scopeList.Count; i++)
            {
                HotkeyScope scope = _scopeList[i];
                // IsInside 是纯托管链扫描，先否决不相关 scope，避免为每个 scope 读原生属性。
                if (!scope.IsInside(focusHolder) || !IsHolderAvailable(scope.Holder))
                    continue;

                if (bestScope == null)
                {
                    bestScope = scope;
                    continue;
                }

                // 嵌套作用域必须优先：leaf 决定分发链，父 scope 抢走 leaf 会让子作用域的注册不可达。
                if (scope.IsInside(bestScope.Holder))
                {
                    bestScope = scope;
                    continue;
                }

                if (bestScope.IsInside(scope.Holder))
                    continue;

                if (IsHigherPriority(scope, bestScope))
                    bestScope = scope;
            }

            return bestScope;
        }

        private void CollectRegisteredTriggers(List<HotkeyComponentBase> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < _scopeList.Count; i++)
                _scopeList[i].AppendTriggers(buffer);
        }

        private static bool IsHolderAvailable(UIHolderObjectBase holder)
        {
            return holder != null && holder.gameObject.activeInHierarchy && holder.IsEnabled;
        }

        private static bool IsHigherPriority(HotkeyScope candidate, HotkeyScope current)
        {
            return InputLayerOrder.IsHigher(
                InputLayerOrder.SortingOrderOf(candidate.SortingCanvas), candidate.ActivationSerial, candidate.HierarchyDepth,
                InputLayerOrder.SortingOrderOf(current.SortingCanvas), current.ActivationSerial, current.HierarchyDepth);
        }
    }
}
#endif
