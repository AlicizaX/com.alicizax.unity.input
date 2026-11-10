#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AlicizaX;
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityEngine.UI
{
    public enum EHotkeyPressType : byte
    {
        Started = 0,
        Performed = 1,
        Canceled = 2
    }

    internal sealed class ReferenceEqualityComparer<TKey> : IEqualityComparer<TKey> where TKey : class
    {
        public static readonly ReferenceEqualityComparer<TKey> Instance = new ReferenceEqualityComparer<TKey>();

        private ReferenceEqualityComparer()
        {
        }

        public bool Equals(TKey x, TKey y) => ReferenceEquals(x, y);

        public int GetHashCode(TKey obj) => RuntimeHelpers.GetHashCode(obj);
    }

    internal readonly struct HotkeySlot
    {
        public readonly HotkeyComponentBase Trigger;

        public HotkeySlot(HotkeyComponentBase trigger)
        {
            Trigger = trigger;
        }
    }

    internal sealed class HotkeyActionSlots
    {
        private readonly HotkeySlot[] _slots = new HotkeySlot[3];
        private byte _occupied;

        public bool IsEmpty => _occupied == 0;

        public bool TryGet(EHotkeyPressType pressType, out HotkeySlot registration)
        {
            int index = (int)pressType;
            if ((_occupied & (1 << index)) == 0)
            {
                registration = default;
                return false;
            }

            registration = _slots[index];
            return true;
        }

        public bool TrySet(EHotkeyPressType pressType, HotkeySlot registration)
        {
            int index = (int)pressType;
            if ((_occupied & (1 << index)) != 0)
            {
                return false;
            }

            _slots[index] = registration;
            _occupied |= (byte)(1 << index);
            return true;
        }

        public bool TryClear(EHotkeyPressType pressType, HotkeyComponentBase trigger)
        {
            int index = (int)pressType;
            if ((_occupied & (1 << index)) == 0 || !ReferenceEquals(_slots[index].Trigger, trigger))
            {
                return false;
            }

            _slots[index] = default;
            _occupied &= (byte)~(1 << index);
            return true;
        }

        public void CollectTriggers(List<HotkeyComponentBase> buffer)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if ((_occupied & (1 << i)) != 0)
                {
                    buffer.Add(_slots[i].Trigger);
                }
            }
        }
    }

    internal sealed class HotkeyScope
    {
        private readonly InputHotkey _owner;
        private Canvas _displayCanvas;
        private bool _displayCanvasResolved;
        private UIHolderObjectBase[] _holderChain = Array.Empty<UIHolderObjectBase>();
        private int _holderChainCount;

        public HotkeyScope(InputHotkey owner, UIHolderObjectBase holder)
        {
            _owner = owner;
            Holder = holder;
            ListIndex = -1;
            SlotsByAction = new Dictionary<InputAction, HotkeyActionSlots>(
                ReferenceEqualityComparer<InputAction>.Instance);
        }

        public readonly UIHolderObjectBase Holder;
        public readonly Dictionary<InputAction, HotkeyActionSlots> SlotsByAction;

        public UIHolderObjectBase ParentHolder { get; private set; }
        public int HierarchyDepth { get; private set; }
        public int ListIndex { get; set; }
        public ulong ActivationSerial;

        public bool IsEmpty => SlotsByAction.Count == 0;

        public Canvas DisplayCanvas
        {
            get
            {
                if (!_displayCanvasResolved || (object)_displayCanvas != null && _displayCanvas == null)
                {
                    ResolveDisplayCanvas();
                }

                return _displayCanvas;
            }
        }

        /// <summary>
        /// 重新计算层级信息并失效 Canvas 缓存。只在创建、注册、窗口显示时调用，不参与每次按键。
        /// 一趟遍历同时得到深度、父 Holder 和祖先链，供 IsInside 做零原生调用的归属判断。
        /// </summary>
        public void RefreshHierarchy()
        {
            if (Holder == null)
            {
                ParentHolder = null;
                HierarchyDepth = 0;
                _holderChainCount = 0;
                InvalidateDisplayCanvas();
                return;
            }

            int depth = 0;
            int holderCount = 0;
            for (Transform current = Holder.transform; current != null; current = current.parent)
            {
                depth++;
                if (current.TryGetComponent(out UIHolderObjectBase ancestor))
                {
                    if (holderCount == _holderChain.Length)
                    {
                        Array.Resize(ref _holderChain, Math.Max(4, holderCount * 2));
                    }

                    _holderChain[holderCount++] = ancestor;
                }
            }

            _holderChainCount = holderCount;
            HierarchyDepth = depth;
            ParentHolder = holderCount > 1 ? _holderChain[1] : null;
            InvalidateDisplayCanvas();
        }

        /// <summary>
        /// 判断本作用域是否位于给定 Holder 自身或其后代之下（沿缓存的 Holder 祖先链比较，不访问 Transform）。
        /// </summary>
        public bool IsInside(UIHolderObjectBase holder)
        {
            for (int i = 0; i < _holderChainCount; i++)
            {
                if (ReferenceEquals(_holderChain[i], holder))
                {
                    return true;
                }
            }

            return false;
        }

        public void OnBeforeShowHandler() => _owner.ActivateScope(Holder);

        public void OnBeforeClosedHandler() => _owner.DeactivateScope(Holder);

        public void OnDestroyHandler() => _owner.DestroyScope(Holder);

        private void InvalidateDisplayCanvas()
        {
            _displayCanvasResolved = false;
            _displayCanvas = null;
        }

        private void ResolveDisplayCanvas()
        {
            _displayCanvasResolved = true;
            _displayCanvas = Holder != null
                ? InputHierarchyUtility.ResolveSortingCanvas(Holder.GetComponentInParent<Canvas>(true))
                : null;
        }
    }

    /// <summary>
    /// 热键模块：把 <see cref="HotkeyComponentBase"/> 注册到所属 Holder 的作用域，按当前焦点链分发按键。
    /// 实例由 <see cref="UXInput"/> 创建并统一启停，模块内部不保存静态状态。
    /// </summary>
    public sealed class InputHotkey
    {
        private sealed class HotkeyActionState
        {
            public int Started;
            public int Performed;
            public int Canceled;
            public bool HasPressTarget;
            public UIHolderObjectBase FocusHolder;
            public HotkeyScope LeafScope;

            public int Total => Started + Performed + Canceled;

            public void Increment(EHotkeyPressType pressType)
            {
                switch (pressType)
                {
                    case EHotkeyPressType.Started:
                        Started++;
                        break;
                    case EHotkeyPressType.Canceled:
                        Canceled++;
                        break;
                    default:
                        Performed++;
                        break;
                }
            }

            public void Decrement(EHotkeyPressType pressType)
            {
                switch (pressType)
                {
                    case EHotkeyPressType.Started:
                        Started--;
                        break;
                    case EHotkeyPressType.Canceled:
                        Canceled--;
                        break;
                    default:
                        Performed--;
                        break;
                }
            }
        }

        private static readonly Predicate<UIHolderObjectBase> HotkeyFocusPredicate = IsHotkeyFocusHolder;

        private readonly Dictionary<InputAction, HotkeyActionState> _actionStates =
            new Dictionary<InputAction, HotkeyActionState>();

        private readonly Dictionary<UIHolderObjectBase, HotkeyScope> _scopesByHolder =
            new Dictionary<UIHolderObjectBase, HotkeyScope>(ReferenceEqualityComparer<UIHolderObjectBase>.Instance);

        private readonly List<HotkeyScope> _scopeList = new List<HotkeyScope>(32);
        private readonly List<HotkeyComponentBase> _triggerBuffer = new List<HotkeyComponentBase>(16);
        private readonly Action<InputAction.CallbackContext> _startedHandler;
        private readonly Action<InputAction.CallbackContext> _performedHandler;
        private readonly Action<InputAction.CallbackContext> _canceledHandler;

        private InputActionAsset _actionsAsset;
        private ulong _activationSerial;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly HashSet<InputAction> _warnedNonButtonActions =
            new HashSet<InputAction>(ReferenceEqualityComparer<InputAction>.Instance);
#endif

        internal InputHotkey(InputActionAsset actions)
        {
            _actionsAsset = actions;

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
            {
                UnregisterHotkey(_triggerBuffer[i]);
            }

            _triggerBuffer.Clear();

            for (int i = _scopeList.Count - 1; i >= 0; i--)
            {
                DetachScope(_scopeList[i]);
            }

            _scopeList.Clear();
            _scopesByHolder.Clear();
            _actionStates.Clear();
            _activationSerial = 0;
            _actionsAsset = null;
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
            foreach (KeyValuePair<InputAction, HotkeyActionState> pair in _actionStates)
            {
                pair.Value.HasPressTarget = false;
                pair.Value.FocusHolder = null;
                pair.Value.LeafScope = null;
            }
        }

        internal void RegisterHotkey(
            HotkeyComponentBase trigger,
            UIHolderObjectBase holder,
            InputActionReference action,
            EHotkeyPressType pressType)
        {
            // Holder 与 InputActionReference 是序列化配置，可能为空；动作也可能已被从资产中移除。
            InputAction referenced = action != null ? action.action : null;
            InputAction inputAction = referenced != null && _actionsAsset != null
                ? _actionsAsset.FindAction(referenced.id)
                : null;
            if (inputAction == null || holder == null)
            {
                return;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (inputAction.type != InputActionType.Button && _warnedNonButtonActions.Add(inputAction))
            {
                Debug.LogWarning(
                    "Hotkey action '" + inputAction.name + "' is " + inputAction.type +
                    ". Hotkeys expect Button actions; value actions dispatch on every value change.",
                    trigger);
            }
#endif

            UnregisterHotkey(trigger);

            HotkeyScope scope = GetOrCreateScope(holder);
            scope.RefreshHierarchy();

            if (!TryAddScopeRegistration(scope, inputAction, pressType, new HotkeySlot(trigger)))
            {
                ReleaseScopeIfEmpty(scope);
                return;
            }

            UpdateSubscription(inputAction, pressType, true);

            if (IsScopeActive(scope))
            {
                scope.ActivationSerial = ++_activationSerial;
            }

            trigger.IsRegistered = true;
            trigger.RegisteredAction = inputAction;
            trigger.RegisteredHolder = holder;
            trigger.RegisteredPressType = pressType;
        }

        internal void UnregisterHotkey(HotkeyComponentBase trigger)
        {
            if (!trigger.IsRegistered)
            {
                return;
            }

            InputAction action = trigger.RegisteredAction;
            UIHolderObjectBase holder = trigger.RegisteredHolder;
            EHotkeyPressType pressType = trigger.RegisteredPressType;
            trigger.IsRegistered = false;
            trigger.RegisteredAction = null;
            trigger.RegisteredHolder = null;

            if (_scopesByHolder.TryGetValue(holder, out HotkeyScope scope)
                && RemoveScopeRegistration(scope, action, pressType, trigger))
            {
                ReleaseScopeIfEmpty(scope);
            }

            UpdateSubscription(action, pressType, false);
        }

        internal void ActivateScope(UIHolderObjectBase holder)
        {
            if (!_scopesByHolder.TryGetValue(holder, out HotkeyScope scope))
            {
                return;
            }

            scope.RefreshHierarchy();
            scope.ActivationSerial = ++_activationSerial;
        }

        internal void DeactivateScope(UIHolderObjectBase holder)
        {
            if (!_scopesByHolder.TryGetValue(holder, out HotkeyScope scope))
            {
                return;
            }

            RemovePressTargetsForHolder(holder);
        }

        internal void DestroyScope(UIHolderObjectBase holder)
        {
            if (!_scopesByHolder.TryGetValue(holder, out HotkeyScope scope))
            {
                return;
            }

            RemovePressTargetsForHolder(holder);
            _triggerBuffer.Clear();
            foreach (KeyValuePair<InputAction, HotkeyActionSlots> pair in scope.SlotsByAction)
            {
                pair.Value.CollectTriggers(_triggerBuffer);
            }

            for (int i = 0; i < _triggerBuffer.Count; i++)
            {
                UnregisterHotkey(_triggerBuffer[i]);
            }

            _triggerBuffer.Clear();

            if (_scopesByHolder.TryGetValue(holder, out HotkeyScope attachedScope) && ReferenceEquals(attachedScope, scope))
            {
                DetachScope(scope);
            }
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
            {
                return scope;
            }

            scope = new HotkeyScope(this, holder);

            holder.OnShowEvent += scope.OnBeforeShowHandler;
            holder.OnCloseEvent += scope.OnBeforeClosedHandler;
            holder.OnDestroyEvent += scope.OnDestroyHandler;

            scope.ListIndex = _scopeList.Count;
            _scopeList.Add(scope);
            _scopesByHolder[holder] = scope;
            return scope;
        }

        private void DetachScope(HotkeyScope scope)
        {
            UIHolderObjectBase holder = scope.Holder;
            if (!ReferenceEquals(holder, null))
            {
                holder.OnShowEvent -= scope.OnBeforeShowHandler;
                holder.OnCloseEvent -= scope.OnBeforeClosedHandler;
                holder.OnDestroyEvent -= scope.OnDestroyHandler;
                _scopesByHolder.Remove(holder);
            }

            RemoveScopeFromList(scope);
        }

        private void RemoveScopeFromList(HotkeyScope scope)
        {
            if (scope.ListIndex < 0)
            {
                return;
            }

            int index = scope.ListIndex;
            int lastIndex = _scopeList.Count - 1;
            HotkeyScope lastScope = _scopeList[lastIndex];
            _scopeList.RemoveAt(lastIndex);
            if (index != lastIndex)
            {
                _scopeList[index] = lastScope;
                lastScope.ListIndex = index;
            }

            scope.ListIndex = -1;
        }

        private void ReleaseScopeIfEmpty(HotkeyScope scope)
        {
            if (scope.IsEmpty)
            {
                DetachScope(scope);
            }
        }

        private static bool TryAddScopeRegistration(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType,
            HotkeySlot registration)
        {
            if (!scope.SlotsByAction.TryGetValue(action, out HotkeyActionSlots actionSlots))
            {
                actionSlots = new HotkeyActionSlots();
                scope.SlotsByAction[action] = actionSlots;
            }

            return actionSlots.TrySet(pressType, registration);
        }

        private static bool RemoveScopeRegistration(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType,
            HotkeyComponentBase trigger)
        {
            if (!scope.SlotsByAction.TryGetValue(action, out HotkeyActionSlots actionSlots)
                || !actionSlots.TryClear(pressType, trigger))
            {
                return false;
            }

            if (actionSlots.IsEmpty)
            {
                scope.SlotsByAction.Remove(action);
            }

            return true;
        }

        private void UpdateSubscription(InputAction action, EHotkeyPressType pressType, bool add)
        {
            if (!_actionStates.TryGetValue(action, out HotkeyActionState state))
            {
                state = new HotkeyActionState();
                _actionStates[action] = state;
            }

            if (add)
            {
                if (state.Total == 0)
                {
                    Subscribe(action, EHotkeyPressType.Started);
                    Subscribe(action, EHotkeyPressType.Canceled);
                }

                if (pressType == EHotkeyPressType.Performed && state.Performed == 0)
                {
                    Subscribe(action, EHotkeyPressType.Performed);
                }

                state.Increment(pressType);
                return;
            }

            state.Decrement(pressType);
            if (pressType == EHotkeyPressType.Performed && state.Performed == 0)
            {
                Unsubscribe(action, EHotkeyPressType.Performed);
            }

            if (state.Total == 0)
            {
                Unsubscribe(action, EHotkeyPressType.Started);
                Unsubscribe(action, EHotkeyPressType.Canceled);
                _actionStates.Remove(action);
            }
        }

        private void Subscribe(InputAction action, EHotkeyPressType pressType)
        {
            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    action.started += _startedHandler;
                    break;
                case EHotkeyPressType.Canceled:
                    action.canceled += _canceledHandler;
                    break;
                default:
                    action.performed += _performedHandler;
                    break;
            }
        }

        private void Unsubscribe(InputAction action, EHotkeyPressType pressType)
        {
            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    action.started -= _startedHandler;
                    break;
                case EHotkeyPressType.Canceled:
                    action.canceled -= _canceledHandler;
                    break;
                default:
                    action.performed -= _performedHandler;
                    break;
            }
        }

        private void Dispatch(InputAction.CallbackContext context, EHotkeyPressType pressType)
        {
            InputAction action = context.action;
            if (action == null || !_actionStates.TryGetValue(action, out HotkeyActionState state))
            {
                return;
            }

            if (pressType == EHotkeyPressType.Started)
            {
                CapturePressTarget(state);
                if (state.Started > 0)
                {
                    TryDispatchToLockedTarget(state, action, pressType);
                }

                return;
            }

            if (pressType == EHotkeyPressType.Canceled)
            {
                if (state.HasPressTarget)
                {
                    if (state.Canceled > 0)
                    {
                        TryDispatchToLockedTarget(state, action, pressType);
                    }

                    state.HasPressTarget = false;
                    state.FocusHolder = null;
                    state.LeafScope = null;
                }

                return;
            }

            if (!state.HasPressTarget)
            {
                CapturePressTarget(state);
            }

            TryDispatchToLockedTarget(state, action, pressType);
        }

        private void CapturePressTarget(HotkeyActionState state)
        {
            if (!TryGetCurrentHotkeyFocusHolder(out UIHolderObjectBase focusHolder))
            {
                state.HasPressTarget = false;
                state.FocusHolder = null;
                state.LeafScope = null;
                return;
            }

            state.HasPressTarget = true;
            state.FocusHolder = focusHolder;
            state.LeafScope = FindTopScopeInsideHolder(focusHolder);
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

        private void TryDispatchToLockedTarget(
            HotkeyActionState state,
            InputAction action,
            EHotkeyPressType pressType)
        {
            if (state.FocusHolder == null || state.LeafScope == null || !IsHolderAvailable(state.FocusHolder))
            {
                return;
            }

            if (!state.LeafScope.IsInside(state.FocusHolder))
            {
                return;
            }

            TryDispatchToScopeChain(state.LeafScope, state.FocusHolder, action, pressType);
        }

        private void RemovePressTargetsForHolder(UIHolderObjectBase holder)
        {
            foreach (KeyValuePair<InputAction, HotkeyActionState> pair in _actionStates)
            {
                HotkeyActionState state = pair.Value;
                if (!state.HasPressTarget)
                {
                    continue;
                }

                if (ReferenceEquals(state.FocusHolder, holder)
                    || InputHierarchyUtility.IsDescendantOrSelf(state.FocusHolder, holder)
                    || state.LeafScope != null && state.LeafScope.IsInside(holder))
                {
                    state.HasPressTarget = false;
                    state.FocusHolder = null;
                    state.LeafScope = null;
                }
            }
        }

        private void TryDispatchToScopeChain(
            HotkeyScope leafScope,
            UIHolderObjectBase stopHolder,
            InputAction action,
            EHotkeyPressType pressType)
        {
            HotkeyScope current = leafScope;
            while (current != null)
            {
                if (IsScopeActive(current) && TryDispatchRegistration(current, action, pressType))
                {
                    return;
                }

                if (ReferenceEquals(current.Holder, stopHolder))
                {
                    return;
                }

                UIHolderObjectBase parentHolder = current.ParentHolder;
                current = parentHolder != null && _scopesByHolder.TryGetValue(parentHolder, out HotkeyScope parentScope)
                    ? parentScope
                    : null;
            }
        }

        private static bool TryDispatchRegistration(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType)
        {
            if (!scope.SlotsByAction.TryGetValue(action, out HotkeyActionSlots actionSlots)
                || !actionSlots.TryGet(pressType, out HotkeySlot registration)
                || registration.Trigger == null
                || !registration.Trigger.isActiveAndEnabled)
            {
                return false;
            }

            registration.Trigger.TriggerHotkey();
            return true;
        }

        private HotkeyScope FindTopScopeInsideHolder(UIHolderObjectBase focusHolder)
        {
            if (!IsHolderAvailable(focusHolder))
            {
                return null;
            }

            HotkeyScope bestScope = null;
            for (int i = 0; i < _scopeList.Count; i++)
            {
                HotkeyScope scope = _scopeList[i];
                if (!IsScopeActive(scope) || !scope.IsInside(focusHolder))
                {
                    continue;
                }

                if (bestScope == null)
                {
                    bestScope = scope;
                    continue;
                }

                if (!ReferenceEquals(scope.Holder, bestScope.Holder)
                    && scope.IsInside(bestScope.Holder))
                {
                    bestScope = scope;
                    continue;
                }

                if (!ReferenceEquals(bestScope.Holder, scope.Holder)
                    && bestScope.IsInside(scope.Holder))
                {
                    continue;
                }

                if (CompareScopePriority(scope, bestScope) < 0)
                {
                    bestScope = scope;
                }
            }

            return bestScope;
        }

        private void CollectRegisteredTriggers(List<HotkeyComponentBase> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < _scopeList.Count; i++)
            {
                foreach (KeyValuePair<InputAction, HotkeyActionSlots> pair in _scopeList[i].SlotsByAction)
                {
                    pair.Value.CollectTriggers(buffer);
                }
            }
        }

        private static bool IsHolderAvailable(UIHolderObjectBase holder)
        {
            return holder != null && holder.gameObject.activeInHierarchy && holder.IsEnabled;
        }

        private static bool IsScopeActive(HotkeyScope scope)
        {
            return IsHolderAvailable(scope.Holder);
        }

        private static int CompareScopePriority(HotkeyScope left, HotkeyScope right)
        {
            Canvas leftCanvas = left.DisplayCanvas;
            Canvas rightCanvas = right.DisplayCanvas;
            int leftOrder = leftCanvas != null ? leftCanvas.sortingOrder : int.MinValue;
            int rightOrder = rightCanvas != null ? rightCanvas.sortingOrder : int.MinValue;
            int orderCompare = rightOrder.CompareTo(leftOrder);
            if (orderCompare != 0)
            {
                return orderCompare;
            }

            int hierarchyCompare = right.HierarchyDepth.CompareTo(left.HierarchyDepth);
            if (hierarchyCompare != 0)
            {
                return hierarchyCompare;
            }

            return right.ActivationSerial.CompareTo(left.ActivationSerial);
        }
    }
}
#endif
