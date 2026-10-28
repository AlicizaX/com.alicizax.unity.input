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

    internal readonly struct HotkeyRegistration
    {
        public readonly HotkeyComponentBase Trigger;
        public readonly bool ConsumesInput;

        public HotkeyRegistration(HotkeyComponentBase trigger, bool consumesInput)
        {
            Trigger = trigger;
            ConsumesInput = consumesInput;
        }
    }

    internal sealed class HotkeyActionRegistrations
    {
        private readonly HotkeyRegistration[] _slots = new HotkeyRegistration[3];
        private byte _occupied;

        public bool IsEmpty => _occupied == 0;

        public bool TryGet(EHotkeyPressType pressType, out HotkeyRegistration registration)
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

        public bool TrySet(EHotkeyPressType pressType, HotkeyRegistration registration, out HotkeyComponentBase existingTrigger)
        {
            int index = (int)pressType;
            if ((_occupied & (1 << index)) != 0)
            {
                existingTrigger = _slots[index].Trigger;
                return false;
            }

            existingTrigger = null;
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
        private Canvas _displayCanvas;
        private bool _displayCanvasResolved;
        private bool _missingDisplayCanvasWarned;

        public HotkeyScope(UIHolderObjectBase holder)
        {
            Holder = holder;
            ListIndex = -1;
            RegistrationsByAction = new Dictionary<InputAction, HotkeyActionRegistrations>(
                ReferenceEqualityComparer<InputAction>.Instance);
        }

        public readonly UIHolderObjectBase Holder;
        public readonly Dictionary<InputAction, HotkeyActionRegistrations> RegistrationsByAction;

        public UIHolderObjectBase ParentHolder { get; private set; }
        public int HierarchyDepth { get; private set; }
        public int ListIndex { get; set; }
        public bool LifecycleActive;
        public ulong ActivationSerial;

        public bool IsEmpty => RegistrationsByAction.Count == 0;

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
        /// </summary>
        public void RefreshHierarchy()
        {
            if (Holder == null)
            {
                ParentHolder = null;
                HierarchyDepth = 0;
                InvalidateDisplayCanvas();
                return;
            }

            HierarchyDepth = InputHierarchyUtility.GetDepth(Holder.transform);
            ParentHolder = InputHierarchyUtility.FindParentHolder(Holder);
            InvalidateDisplayCanvas();
        }

        public void OnBeforeShowHandler() => UXHotkeySystem.ActivateScope(Holder);

        public void OnBeforeClosedHandler() => UXHotkeySystem.DeactivateScope(Holder);

        public void OnDestroyHandler() => UXHotkeySystem.DestroyScope(Holder);

        private void InvalidateDisplayCanvas()
        {
            _displayCanvasResolved = false;
            _displayCanvas = null;
        }

        private void ResolveDisplayCanvas()
        {
            _displayCanvasResolved = true;
            _displayCanvas = Holder != null
                ? Holder.GetComponent<Canvas>() ?? Holder.GetComponentInParent<Canvas>(true)
                : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_displayCanvas == null && Holder != null && !_missingDisplayCanvasWarned)
            {
                _missingDisplayCanvasWarned = true;
                Log.Warning(
                    "Hotkey scope on holder '" + Holder.name + "' has no Canvas in parents. " +
                    "Visibility falls back to lifecycle active + activeInHierarchy; sorting priority is reduced.");
            }
#endif
        }
    }

    internal static class UXHotkeySystem
    {
        private sealed class ActionState
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

        private static readonly Dictionary<InputAction, ActionState> Actions =
            new Dictionary<InputAction, ActionState>();

        private static readonly Dictionary<UIHolderObjectBase, HotkeyScope> Scopes =
            new Dictionary<UIHolderObjectBase, HotkeyScope>(ReferenceEqualityComparer<UIHolderObjectBase>.Instance);

        private static readonly List<HotkeyScope> ScopeList = new List<HotkeyScope>(32);
        private static readonly List<HotkeyComponentBase> ScratchTriggers = new List<HotkeyComponentBase>(16);

        private static readonly Action<InputAction.CallbackContext> StartedHandler = OnActionStarted;
        private static readonly Action<InputAction.CallbackContext> PerformedHandler = OnActionPerformed;
        private static readonly Action<InputAction.CallbackContext> CanceledHandler = OnActionCanceled;
        private static readonly Predicate<UIHolderObjectBase> HotkeyFocusPredicate = IsHotkeyFocusHolder;

        private static ulong _activationSerial;
        private static HotkeyAppHookRunner _appHookRunner;

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        private static void ClearHotkeyRegistry()
        {
            Shutdown();
        }
#endif

        /// <summary>
        /// 清空全部热键注册、作用域和 App Hook。由 InputActionProvider 关闭时调用。
        /// </summary>
        internal static void Shutdown()
        {
            CollectRegisteredTriggers(ScratchTriggers);
            for (int i = 0; i < ScratchTriggers.Count; i++)
            {
                UnregisterHotkey(ScratchTriggers[i]);
            }

            ScratchTriggers.Clear();

            for (int i = ScopeList.Count - 1; i >= 0; i--)
            {
                DetachScope(ScopeList[i]);
            }

            ScopeList.Clear();
            Scopes.Clear();
            Actions.Clear();
            _activationSerial = 0;
            DestroyAppHooks();
        }

        internal static void ResetForPlayMode()
        {
            Shutdown();
        }

        internal static void ResetTransientState()
        {
            foreach (KeyValuePair<InputAction, ActionState> pair in Actions)
            {
                pair.Value.HasPressTarget = false;
                pair.Value.FocusHolder = null;
                pair.Value.LeafScope = null;
            }
        }

        internal static void RegisterHotkey(
            HotkeyComponentBase trigger,
            UIHolderObjectBase holder,
            InputActionReference action,
            EHotkeyPressType pressType)
        {
            if (!TryResolveRuntimeAction(action, out InputAction inputAction, out string resolvePath))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Log.Warning(
                    "Hotkey registration skipped on '" + GetTriggerGameObjectName(trigger) + "': " +
                    "could not resolve InputAction from reference (path='" + (resolvePath ?? "<null>") + "'). " +
                    "Ensure InputActionProvider is initialized with the same InputActionAsset.");
#endif
                return;
            }

            UnregisterHotkey(trigger);

            HotkeyScope scope = GetOrCreateScope(holder);
            scope.RefreshHierarchy();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            WarnIfObservingDisabledAction(trigger, inputAction, resolvePath);
#endif
            if (!TryAddScopeRegistration(scope, inputAction, pressType, new HotkeyRegistration(trigger, trigger.HotkeyConsumesInput)))
            {
                ReleaseScopeIfEmpty(scope);
                return;
            }

            AdjustSubscription(inputAction, pressType, true);

            if (scope.LifecycleActive)
            {
                scope.ActivationSerial = ++_activationSerial;
            }

            trigger.IsRegistered = true;
            trigger.RegisteredAction = inputAction;
            trigger.RegisteredHolder = holder;
            trigger.RegisteredPressType = pressType;
        }

        internal static void UnregisterHotkey(HotkeyComponentBase trigger)
        {
            if ((object)trigger == null || !trigger.IsRegistered)
            {
                return;
            }

            InputAction action = trigger.RegisteredAction;
            UIHolderObjectBase holder = trigger.RegisteredHolder;
            EHotkeyPressType pressType = trigger.RegisteredPressType;
            trigger.IsRegistered = false;
            trigger.RegisteredAction = null;
            trigger.RegisteredHolder = null;

            if (Scopes.TryGetValue(holder, out HotkeyScope scope)
                && RemoveScopeRegistration(scope, action, pressType, trigger))
            {
                ReleaseScopeIfEmpty(scope);
            }

            AdjustSubscription(action, pressType, false);
        }

        internal static void ActivateScope(UIHolderObjectBase holder)
        {
            if (!Scopes.TryGetValue(holder, out HotkeyScope scope))
            {
                return;
            }

            scope.RefreshHierarchy();
            scope.LifecycleActive = true;
            scope.ActivationSerial = ++_activationSerial;
        }

        internal static void DeactivateScope(UIHolderObjectBase holder)
        {
            if (!Scopes.TryGetValue(holder, out HotkeyScope scope))
            {
                return;
            }

            scope.LifecycleActive = false;
            RemovePressTargetsForHolder(holder);
        }

        internal static void DestroyScope(UIHolderObjectBase holder)
        {
            if (!Scopes.TryGetValue(holder, out HotkeyScope scope))
            {
                return;
            }

            RemovePressTargetsForHolder(holder);
            ScratchTriggers.Clear();
            foreach (KeyValuePair<InputAction, HotkeyActionRegistrations> pair in scope.RegistrationsByAction)
            {
                pair.Value.CollectTriggers(ScratchTriggers);
            }

            for (int i = 0; i < ScratchTriggers.Count; i++)
            {
                UnregisterHotkey(ScratchTriggers[i]);
            }

            ScratchTriggers.Clear();

            if (Scopes.TryGetValue(holder, out HotkeyScope attachedScope) && ReferenceEquals(attachedScope, scope))
            {
                DetachScope(scope);
            }
        }

        private static void EnsureAppHooks()
        {
            if (_appHookRunner != null)
            {
                return;
            }

            var go = new GameObject("[UXHotkeySystem]");
            if (Application.isPlaying)
            {
                UnityEngine.Object.DontDestroyOnLoad(go);
            }

            go.hideFlags = HideFlags.HideAndDontSave;
            _appHookRunner = go.AddComponent<HotkeyAppHookRunner>();
        }

        private static void DestroyAppHooks()
        {
            if (_appHookRunner == null)
            {
                return;
            }

            GameObject hookObject = _appHookRunner.gameObject;
            _appHookRunner = null;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(hookObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(hookObject);
            }
        }

        private sealed class HotkeyAppHookRunner : MonoBehaviour
        {
            private void OnApplicationFocus(bool hasFocus)
            {
                if (!hasFocus)
                {
                    ResetTransientState();
                }
            }

            private void OnApplicationPause(bool pauseStatus)
            {
                if (pauseStatus)
                {
                    ResetTransientState();
                }
            }
        }

        private static void OnActionStarted(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Started);

        private static void OnActionPerformed(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Performed);

        private static void OnActionCanceled(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Canceled);

        private static HotkeyScope GetOrCreateScope(UIHolderObjectBase holder)
        {
            if (Scopes.TryGetValue(holder, out HotkeyScope scope))
            {
                return scope;
            }

            scope = new HotkeyScope(holder);
            scope.LifecycleActive = IsScopeVisible(scope);

            holder.OnWindowBeforeShowEvent += scope.OnBeforeShowHandler;
            holder.OnWindowBeforeClosedEvent += scope.OnBeforeClosedHandler;
            holder.OnWindowDestroyEvent += scope.OnDestroyHandler;

            scope.ListIndex = ScopeList.Count;
            ScopeList.Add(scope);
            Scopes[holder] = scope;
            return scope;
        }

        private static void DetachScope(HotkeyScope scope)
        {
            UIHolderObjectBase holder = scope.Holder;
            if (!ReferenceEquals(holder, null))
            {
                holder.OnWindowBeforeShowEvent -= scope.OnBeforeShowHandler;
                holder.OnWindowBeforeClosedEvent -= scope.OnBeforeClosedHandler;
                holder.OnWindowDestroyEvent -= scope.OnDestroyHandler;
                Scopes.Remove(holder);
            }

            RemoveScopeFromList(scope);
        }

        private static void RemoveScopeFromList(HotkeyScope scope)
        {
            if (scope.ListIndex < 0)
            {
                return;
            }

            int index = scope.ListIndex;
            int lastIndex = ScopeList.Count - 1;
            HotkeyScope lastScope = ScopeList[lastIndex];
            ScopeList.RemoveAt(lastIndex);
            if (index != lastIndex)
            {
                ScopeList[index] = lastScope;
                lastScope.ListIndex = index;
            }

            scope.ListIndex = -1;
        }

        private static void ReleaseScopeIfEmpty(HotkeyScope scope)
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
            HotkeyRegistration registration)
        {
            if (!scope.RegistrationsByAction.TryGetValue(action, out HotkeyActionRegistrations actionRegistrations))
            {
                actionRegistrations = new HotkeyActionRegistrations();
                scope.RegistrationsByAction[action] = actionRegistrations;
            }

            if (!actionRegistrations.TrySet(pressType, registration, out HotkeyComponentBase existingTrigger))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                WarnRegistrationConflict(scope, action, pressType, existingTrigger, registration.Trigger);
#endif
                return false;
            }

            return true;
        }

        private static bool RemoveScopeRegistration(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType,
            HotkeyComponentBase trigger)
        {
            if (!scope.RegistrationsByAction.TryGetValue(action, out HotkeyActionRegistrations actionRegistrations)
                || !actionRegistrations.TryClear(pressType, trigger))
            {
                return false;
            }

            if (actionRegistrations.IsEmpty)
            {
                scope.RegistrationsByAction.Remove(action);
            }

            return true;
        }

        private static void AdjustSubscription(InputAction action, EHotkeyPressType pressType, bool add)
        {
            if (!Actions.TryGetValue(action, out ActionState state))
            {
                state = new ActionState();
                Actions[action] = state;
            }

            if (add)
            {
                if (state.Total == 0)
                {
                    Subscribe(action, EHotkeyPressType.Started);
                    Subscribe(action, EHotkeyPressType.Canceled);
                    EnsureAppHooks();
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
                Actions.Remove(action);
            }
        }

        private static void Subscribe(InputAction action, EHotkeyPressType pressType)
        {
            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    action.started += StartedHandler;
                    break;
                case EHotkeyPressType.Canceled:
                    action.canceled += CanceledHandler;
                    break;
                default:
                    action.performed += PerformedHandler;
                    break;
            }
        }

        private static void Unsubscribe(InputAction action, EHotkeyPressType pressType)
        {
            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    action.started -= StartedHandler;
                    break;
                case EHotkeyPressType.Canceled:
                    action.canceled -= CanceledHandler;
                    break;
                default:
                    action.performed -= PerformedHandler;
                    break;
            }
        }

        private static void Dispatch(InputAction.CallbackContext context, EHotkeyPressType pressType)
        {
            InputAction action = context.action;
            if (action == null || !Actions.TryGetValue(action, out ActionState state))
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

        private static void CapturePressTarget(ActionState state)
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

        private static void TryDispatchToLockedTarget(
            ActionState state,
            InputAction action,
            EHotkeyPressType pressType)
        {
            if (state.FocusHolder == null || state.LeafScope == null || !IsHolderAvailable(state.FocusHolder))
            {
                return;
            }

            if (!InputHierarchyUtility.IsDescendantOrSelf(state.LeafScope.Holder, state.FocusHolder))
            {
                return;
            }

            TryDispatchToScopeChain(state.LeafScope, state.FocusHolder, action, pressType);
        }

        private static void RemovePressTargetsForHolder(UIHolderObjectBase holder)
        {
            foreach (KeyValuePair<InputAction, ActionState> pair in Actions)
            {
                ActionState state = pair.Value;
                if (!state.HasPressTarget)
                {
                    continue;
                }

                if (ReferenceEquals(state.FocusHolder, holder)
                    || InputHierarchyUtility.IsDescendantOrSelf(state.FocusHolder, holder)
                    || state.LeafScope != null
                    && InputHierarchyUtility.IsDescendantOrSelf(state.LeafScope.Holder, holder))
                {
                    state.HasPressTarget = false;
                    state.FocusHolder = null;
                    state.LeafScope = null;
                }
            }
        }

        private static void TryDispatchToScopeChain(
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
                current = parentHolder != null && Scopes.TryGetValue(parentHolder, out HotkeyScope parentScope)
                    ? parentScope
                    : null;
            }
        }

        private static bool TryDispatchRegistration(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType)
        {
            if (!scope.RegistrationsByAction.TryGetValue(action, out HotkeyActionRegistrations actionRegistrations)
                || !actionRegistrations.TryGet(pressType, out HotkeyRegistration registration)
                || registration.Trigger == null
                || !registration.Trigger.isActiveAndEnabled)
            {
                return false;
            }

            registration.Trigger.HotkeyActionTrigger();
            return registration.ConsumesInput;
        }

        private static HotkeyScope FindTopScopeInsideHolder(UIHolderObjectBase focusHolder)
        {
            if (!IsHolderAvailable(focusHolder))
            {
                return null;
            }

            HotkeyScope bestScope = null;
            for (int i = 0; i < ScopeList.Count; i++)
            {
                HotkeyScope scope = ScopeList[i];
                if (!IsScopeActive(scope) || !InputHierarchyUtility.IsDescendantOrSelf(scope.Holder, focusHolder))
                {
                    continue;
                }

                if (bestScope == null)
                {
                    bestScope = scope;
                    continue;
                }

                if (!ReferenceEquals(scope.Holder, bestScope.Holder)
                    && InputHierarchyUtility.IsDescendantOrSelf(scope.Holder, bestScope.Holder))
                {
                    bestScope = scope;
                    continue;
                }

                if (!ReferenceEquals(bestScope.Holder, scope.Holder)
                    && InputHierarchyUtility.IsDescendantOrSelf(bestScope.Holder, scope.Holder))
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

        private static void CollectRegisteredTriggers(List<HotkeyComponentBase> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < ScopeList.Count; i++)
            {
                foreach (KeyValuePair<InputAction, HotkeyActionRegistrations> pair in ScopeList[i].RegistrationsByAction)
                {
                    pair.Value.CollectTriggers(buffer);
                }
            }
        }

        private static bool IsHolderAvailable(UIHolderObjectBase holder)
        {
            return holder != null && holder.IsValid() && holder.gameObject.activeInHierarchy;
        }

        private static bool IsScopeActive(HotkeyScope scope)
        {
            return scope.LifecycleActive && IsScopeVisible(scope);
        }

        private static bool IsScopeVisible(HotkeyScope scope)
        {
            UIHolderObjectBase holder = scope.Holder;
            if (holder == null || !holder.IsValid() || !holder.gameObject.activeInHierarchy)
            {
                return false;
            }

            Canvas displayCanvas = scope.DisplayCanvas;
            return displayCanvas == null || displayCanvas.gameObject.layer == UIComponent.UIShowLayer;
        }

        private static int CompareScopePriority(HotkeyScope left, HotkeyScope right)
        {
            int leftOrder = left.DisplayCanvas != null ? left.DisplayCanvas.sortingOrder : int.MinValue;
            int rightOrder = right.DisplayCanvas != null ? right.DisplayCanvas.sortingOrder : int.MinValue;
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

        private static bool TryResolveRuntimeAction(
            InputActionReference actionReference,
            out InputAction inputAction,
            out string resolvePath)
        {
            inputAction = null;
            resolvePath = null;

            InputAction referenceAction = actionReference != null ? actionReference.action : null;
            if (referenceAction == null)
            {
                return false;
            }

            InputActionMap map = referenceAction.actionMap;
            if (map == null || string.IsNullOrEmpty(map.name) || string.IsNullOrEmpty(referenceAction.name))
            {
                return false;
            }

            InputActionAsset providerActions = InputActionProvider.Actions;
            if (providerActions != null && ReferenceEquals(map.asset, providerActions))
            {
                inputAction = referenceAction;
                return true;
            }

            // 运行时克隆资产时引用指向源资产，回退到按路径解析。该字符串只在注册期分配一次。
            resolvePath = map.name + "/" + referenceAction.name;
            if (!InputActionProvider.TryResolveAction(resolvePath, out inputAction))
            {
                inputAction = null;
                return false;
            }

            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static void WarnIfObservingDisabledAction(
            HotkeyComponentBase trigger,
            InputAction action,
            string resolvePath)
        {
            if (action.enabled)
            {
                return;
            }

            string actionLabel = !string.IsNullOrEmpty(resolvePath) ? resolvePath : (action.name ?? "<null>");
            Log.Warning(
                GetTriggerGameObjectName(trigger) + " observes disabled hotkey action '" + actionLabel + "'. " +
                "The hotkey system will not enable it; make sure InputActionProvider enabled the owning map.");
        }

        private static void WarnRegistrationConflict(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType,
            HotkeyComponentBase registeredTrigger,
            HotkeyComponentBase rejectedTrigger)
        {
            string actionName = action != null ? action.name : "<null>";
            string holderName = scope.Holder != null ? scope.Holder.name : "<null>";
            Log.Warning(
                GetTriggerGameObjectName(rejectedTrigger) + " repeated hotkey registration for " + actionName +
                " on holder " + holderName + " (" + pressType + "). " +
                "Existing registration on " + GetTriggerGameObjectName(registeredTrigger) + " keeps working; duplicate registration is ignored. " +
                "Disable the previous widget or component before registering another hotkey for the same holder, action, and press type.");
        }

        private static string GetTriggerGameObjectName(HotkeyComponentBase trigger)
        {
            return trigger != null ? trigger.gameObject.name : "<null>";
        }
#endif
    }

    public static class UXHotkeyExtension
    {
        public static void BindHotKey(this HotkeyComponentBase trigger)
        {
            InputActionReference action = trigger.HotkeyAction;
            if (action == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Log.Warning("Hotkey bind skipped on '" + trigger.gameObject.name + "': InputActionReference is not assigned.");
#endif
                return;
            }

            UIHolderObjectBase holder = trigger.HotkeyHolder;
            if (holder == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Log.Warning("Hotkey bind skipped on '" + trigger.gameObject.name + "': UIHolderObjectBase owner not found.");
#endif
                return;
            }

            UXHotkeySystem.RegisterHotkey(trigger, holder, action, trigger.HotkeyPressType);
        }

        public static void UnBindHotKey(this HotkeyComponentBase trigger)
        {
            UXHotkeySystem.UnregisterHotkey(trigger);
        }
    }
}
#endif
