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

    public enum EHotkeyActionOwnershipMode : byte
    {
        ObserveOnly = 0,
        EnableWhileRegistered = 1
    }

    internal sealed class ReferenceEqualityComparer<TKey> : IEqualityComparer<TKey> where TKey : class
    {
        public static readonly ReferenceEqualityComparer<TKey> Instance = new();

        private ReferenceEqualityComparer()
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(TKey x, TKey y)
        {
            return ReferenceEquals(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(TKey obj)
        {
            return obj != null ? RuntimeHelpers.GetHashCode(obj) : 0;
        }
    }

    internal readonly struct HotkeyRegistration
    {
        public readonly IHotkeyTrigger Trigger;
        public readonly bool ConsumesInput;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HotkeyRegistration(
            IHotkeyTrigger trigger,
            bool consumesInput)
        {
            Trigger = trigger;
            ConsumesInput = consumesInput;
        }
    }

    internal sealed class HotkeyActionRegistrations
    {
        public readonly HotkeyRegistrationList StartedRegistrations = new();
        public readonly HotkeyRegistrationList PerformedRegistrations = new();
        public readonly HotkeyRegistrationList CanceledRegistrations = new();

        public bool IsEmpty =>
            StartedRegistrations.Count == 0
            && PerformedRegistrations.Count == 0
            && CanceledRegistrations.Count == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HotkeyRegistrationList GetRegistrations(EHotkeyPressType pressType)
        {
            return pressType switch
            {
                EHotkeyPressType.Started => StartedRegistrations,
                EHotkeyPressType.Canceled => CanceledRegistrations,
                _ => PerformedRegistrations
            };
        }
    }

    internal sealed class HotkeyRegistrationList
    {
        private HotkeyRegistration[] _items = Array.Empty<HotkeyRegistration>();

        public int Count { get; private set; }

        public HotkeyRegistration this[int index] => _items[index];

        public void Add(HotkeyRegistration registration)
        {
            if (Count == _items.Length)
            {
                int newLength = _items.Length == 0 ? 2 : _items.Length << 1;
                Array.Resize(ref _items, newLength);
            }

            _items[Count++] = registration;
        }

        public bool Remove(IHotkeyTrigger trigger, out HotkeyRegistration removedRegistration)
        {
            for (int i = 0; i < Count; i++)
            {
                if (!ReferenceEquals(_items[i].Trigger, trigger))
                {
                    continue;
                }

                removedRegistration = _items[i];
                int lastIndex = Count - 1;
                for (int moveIndex = i; moveIndex < lastIndex; moveIndex++)
                {
                    _items[moveIndex] = _items[moveIndex + 1];
                }

                Count--;
                _items[Count] = default;
                return true;
            }

            removedRegistration = default;
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < Count; i++)
            {
                _items[i] = default;
            }

            Count = 0;
        }
    }

    internal sealed class HotkeyScope
    {
        private Canvas _canvas;

        public HotkeyScope(UIHolderObjectBase holder)
        {
            Holder = holder;
            ListIndex = -1;
            RegistrationsByAction = new Dictionary<InputAction, HotkeyActionRegistrations>(ReferenceEqualityComparer<InputAction>.Instance);
            RefreshHierarchy();
        }

        public readonly UIHolderObjectBase Holder;
        public readonly Dictionary<InputAction, HotkeyActionRegistrations> RegistrationsByAction;

        public UIHolderObjectBase ParentHolder { get; private set; }
        public int HierarchyDepth { get; private set; }
        public int ListIndex { get; set; }
        public bool LifecycleActive;
        public ulong ActivationSerial;

        public bool IsEmpty => RegistrationsByAction.Count == 0;

        public Canvas Canvas
        {
            get
            {
                if (_canvas == null && Holder != null)
                {
                    _canvas = Holder.GetComponent<Canvas>();
                }

                return _canvas;
            }
        }

        public void RefreshHierarchy()
        {
            if (Holder == null)
            {
                ParentHolder = null;
                HierarchyDepth = 0;
                return;
            }

            HierarchyDepth = GetHierarchyDepth(Holder.transform);
            ParentHolder = UXHotkeyRegisterManager.FindParentHolder(Holder);
        }

        public void OnBeforeShowHandler()
        {
            UXHotkeyRegisterManager.ActivateScope(Holder);
        }

        public void OnBeforeClosedHandler()
        {
            UXHotkeyRegisterManager.DeactivateScope(Holder);
        }

        public void OnDestroyHandler()
        {
            UXHotkeyRegisterManager.DestroyScope(Holder);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetHierarchyDepth(Transform current)
        {
            int depth = 0;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }
    }

    internal sealed class ActionRegistrationBucket
    {
        public InputAction Action;
        public int StartedCount;
        public int PerformedCount;
        public int CanceledCount;
        public int ManagedOwnershipCount;
        public bool WasEnabledBeforeHotkey;
        public bool EnabledByHotkeySystem;

        public int TotalCount => StartedCount + PerformedCount + CanceledCount;
    }

    internal readonly struct TriggerRegistration
    {
        public readonly InputAction Action;
        public readonly UIHolderObjectBase Holder;
        public readonly EHotkeyPressType PressType;
        public readonly EHotkeyActionOwnershipMode OwnershipMode;
        public readonly IHotkeyTrigger Trigger;

        public TriggerRegistration(
            InputAction action,
            UIHolderObjectBase holder,
            EHotkeyPressType pressType,
            EHotkeyActionOwnershipMode ownershipMode,
            IHotkeyTrigger trigger)
        {
            Action = action;
            Holder = holder;
            PressType = pressType;
            OwnershipMode = ownershipMode;
            Trigger = trigger;
        }
    }

    internal readonly struct HotkeyPressTarget
    {
        public readonly UIHolderObjectBase FocusHolder;
        public readonly HotkeyScope LeafScope;

        public HotkeyPressTarget(UIHolderObjectBase focusHolder, HotkeyScope leafScope)
        {
            FocusHolder = focusHolder;
            LeafScope = leafScope;
        }

        public bool HasFocus => FocusHolder != null;
    }

    internal static class UXHotkeyRegisterManager
    {
        private static readonly Dictionary<InputAction, ActionRegistrationBucket> _actions =
            new(ReferenceEqualityComparer<InputAction>.Instance);

        private static readonly Dictionary<InputAction, HotkeyPressTarget> _pressTargets =
            new(ReferenceEqualityComparer<InputAction>.Instance);

        private static readonly Dictionary<IHotkeyTrigger, TriggerRegistration> _triggerMap =
            new(ReferenceEqualityComparer<IHotkeyTrigger>.Instance);

        private static readonly Dictionary<UIHolderObjectBase, HotkeyScope> _scopes =
            new(ReferenceEqualityComparer<UIHolderObjectBase>.Instance);

        private static readonly List<HotkeyScope> _scopeList = new(32);

        private static readonly HashSet<UIHolderObjectBase> _ancestorHolders =
            new(ReferenceEqualityComparer<UIHolderObjectBase>.Instance);

        private static IHotkeyTrigger[] _destroyScopeTriggers = Array.Empty<IHotkeyTrigger>();
        private static int _destroyScopeTriggerCount;
        private static InputAction[] _pressTargetRemovalBuffer = Array.Empty<InputAction>();
        private static int _pressTargetRemovalCount;

        private static readonly Action<InputAction.CallbackContext> _startedHandler = OnActionStarted;
        private static readonly Action<InputAction.CallbackContext> _performedHandler = OnActionPerformed;
        private static readonly Action<InputAction.CallbackContext> _canceledHandler = OnActionCanceled;
        private static readonly Predicate<UIHolderObjectBase> _hotkeyFocusPredicate = IsHotkeyFocusHolder;

        private static ulong _activationSerial;
        private static bool _scopeDirty = true;
        private static bool _isDestroyingScope;

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        internal static void ClearHotkeyRegistry()
        {
            IHotkeyTrigger[] triggers = new IHotkeyTrigger[_triggerMap.Count];
            int index = 0;
            foreach (var pair in _triggerMap)
            {
                triggers[index++] = pair.Key;
            }

            for (int i = 0; i < index; i++)
            {
                UnregisterHotkey(triggers[i]);
            }

            _actions.Clear();
            _pressTargets.Clear();
            _triggerMap.Clear();
            _scopes.Clear();
            _scopeList.Clear();
            _ancestorHolders.Clear();
            Array.Clear(_destroyScopeTriggers, 0, _destroyScopeTriggerCount);
            _destroyScopeTriggerCount = 0;
            Array.Clear(_pressTargetRemovalBuffer, 0, _pressTargetRemovalCount);
            _pressTargetRemovalCount = 0;
            _activationSerial = 0;
            _isDestroyingScope = false;
            MarkScopeDirty();
            RebuildScopeCacheImmediate();
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void RegisterHotkey(IHotkeyTrigger trigger, UIHolderObjectBase holder, InputActionReference action, EHotkeyPressType pressType)
        {
            if (trigger == null || holder == null || action == null || action.action == null)
            {
                return;
            }

            UnregisterHotkey(trigger);

            InputAction inputAction = action.action;
            ActionRegistrationBucket bucket = GetOrCreateBucket(inputAction);

            HotkeyScope scope = GetOrCreateScope(holder);
            EHotkeyActionOwnershipMode ownershipMode = trigger.HotkeyActionOwnershipMode;
#if UNITY_EDITOR
            WarnIfObservingDisabledAction(trigger, inputAction, ownershipMode);
#endif
            HotkeyRegistration registration = new(
                trigger,
                trigger.HotkeyConsumesInput);

            if (!TryAddScopeRegistration(scope, inputAction, pressType, registration))
            {
                ReleaseScopeIfEmpty(scope);
                return;
            }

            AdjustBucketSubscription(bucket, pressType, ownershipMode, true);

            if (scope.LifecycleActive)
            {
                scope.ActivationSerial = ++_activationSerial;
            }

            _triggerMap[trigger] = new TriggerRegistration(inputAction, holder, pressType, ownershipMode, trigger);
            MarkScopeDirty();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void UnregisterHotkey(IHotkeyTrigger trigger)
        {
            if (trigger == null || !_triggerMap.TryGetValue(trigger, out var triggerRegistration))
            {
                return;
            }

            HotkeyScope scope = null;
            bool removedFromScope = false;
            if (_scopes.TryGetValue(triggerRegistration.Holder, out scope))
            {
                removedFromScope = RemoveScopeRegistration(scope, triggerRegistration.Action, triggerRegistration.PressType, trigger);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            else
            {
                Log.Error("Hotkey registry is inconsistent: scope missing during unregister.");
            }

            if (!removedFromScope)
            {
                Log.Error("Hotkey registry is inconsistent: trigger slot missing during unregister.");
            }
#endif

            _triggerMap.Remove(trigger);

            if (_actions.TryGetValue(triggerRegistration.Action, out var bucket))
            {
                RemoveActionRegistration(bucket, triggerRegistration.PressType, triggerRegistration.Action, triggerRegistration.OwnershipMode);
            }

            ReleaseScopeIfEmpty(scope);
            MarkScopeDirty();
        }

        internal static void ActivateScope(UIHolderObjectBase holder)
        {
            if (_scopes.TryGetValue(holder, out var scope))
            {
                scope.LifecycleActive = true;
                scope.ActivationSerial = ++_activationSerial;
                MarkScopeDirty();
            }
        }

        internal static void DeactivateScope(UIHolderObjectBase holder)
        {
            if (_scopes.TryGetValue(holder, out var scope))
            {
                scope.LifecycleActive = false;
                RemovePressTargetsForHolder(holder);
                MarkScopeDirty();
            }
        }

        internal static void DestroyScope(UIHolderObjectBase holder)
        {
            if (holder == null || _isDestroyingScope || !_scopes.TryGetValue(holder, out var scope))
            {
                return;
            }

            _isDestroyingScope = true;
            try
            {
                RemovePressTargetsForHolder(holder);
                _destroyScopeTriggerCount = 0;
                foreach (var pair in scope.RegistrationsByAction)
                {
                    CollectTriggers(pair.Value.StartedRegistrations);
                    CollectTriggers(pair.Value.PerformedRegistrations);
                    CollectTriggers(pair.Value.CanceledRegistrations);
                }

                for (int i = 0; i < _destroyScopeTriggerCount; i++)
                {
                    UnregisterHotkey(_destroyScopeTriggers[i]);
                }

                Array.Clear(_destroyScopeTriggers, 0, _destroyScopeTriggerCount);
                _destroyScopeTriggerCount = 0;

                if (_scopes.TryGetValue(holder, out var attachedScope) && ReferenceEquals(attachedScope, scope))
                {
                    DetachScope(scope);
                }
            }
            finally
            {
                _isDestroyingScope = false;
            }
        }

        internal static UIHolderObjectBase FindParentHolder(UIHolderObjectBase holder)
        {
            if (holder == null)
            {
                return null;
            }

            Transform current = holder.transform.parent;
            while (current != null)
            {
                if (current.TryGetComponent<UIHolderObjectBase>(out var parentHolder))
                {
                    return parentHolder;
                }

                current = current.parent;
            }

            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void OnActionStarted(InputAction.CallbackContext context)
        {
            Dispatch(context, EHotkeyPressType.Started);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void OnActionPerformed(InputAction.CallbackContext context)
        {
            Dispatch(context, EHotkeyPressType.Performed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void OnActionCanceled(InputAction.CallbackContext context)
        {
            Dispatch(context, EHotkeyPressType.Canceled);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ActionRegistrationBucket GetOrCreateBucket(InputAction inputAction)
        {
            if (_actions.TryGetValue(inputAction, out var bucket))
            {
                return bucket;
            }

            bucket = new ActionRegistrationBucket { Action = inputAction };
            _actions[inputAction] = bucket;
            return bucket;
        }

        private static HotkeyScope GetOrCreateScope(UIHolderObjectBase holder)
        {
            if (_scopes.TryGetValue(holder, out var scope))
            {
                return scope;
            }

            scope = new HotkeyScope(holder)
            {
                ActivationSerial = ++_activationSerial,
                LifecycleActive = false
            };
            scope.LifecycleActive = IsScopeVisible(scope);

            holder.OnWindowBeforeShowEvent += scope.OnBeforeShowHandler;
            holder.OnWindowBeforeClosedEvent += scope.OnBeforeClosedHandler;
            holder.OnWindowDestroyEvent += scope.OnDestroyHandler;

            scope.ListIndex = _scopeList.Count;
            _scopeList.Add(scope);
            _scopes[holder] = scope;
            MarkScopeDirty();
            return scope;
        }

        private static void DetachScope(HotkeyScope scope)
        {
            if (scope == null)
            {
                return;
            }

            UIHolderObjectBase holder = scope.Holder;
            if (!ReferenceEquals(holder, null))
            {
                holder.OnWindowBeforeShowEvent -= scope.OnBeforeShowHandler;
                holder.OnWindowBeforeClosedEvent -= scope.OnBeforeClosedHandler;
                holder.OnWindowDestroyEvent -= scope.OnDestroyHandler;
                _scopes.Remove(holder);
            }

            RemoveScopeFromList(scope);
            MarkScopeDirty();
        }

        private static void RemoveScopeFromList(HotkeyScope scope)
        {
            int index = scope.ListIndex;
            if ((uint)index >= (uint)_scopeList.Count || !ReferenceEquals(_scopeList[index], scope))
            {
                index = _scopeList.IndexOf(scope);
                if (index < 0)
                {
                    scope.ListIndex = -1;
                    return;
                }
            }

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

        private static void ReleaseScopeIfEmpty(HotkeyScope scope)
        {
            if (scope != null && scope.IsEmpty)
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
            if (!scope.RegistrationsByAction.TryGetValue(action, out var actionRegistrations))
            {
                actionRegistrations = new HotkeyActionRegistrations();
                scope.RegistrationsByAction[action] = actionRegistrations;
            }

            HotkeyRegistrationList registrations = actionRegistrations.GetRegistrations(pressType);
            if (registrations.Count > 0)
            {
#if UNITY_EDITOR
                WarnRegistrationConflict(scope, action, pressType, registrations[0].Trigger, registration.Trigger);
#endif
                return false;
            }

            registrations.Add(registration);
            return true;
        }

        private static bool RemoveScopeRegistration(HotkeyScope scope, InputAction action, EHotkeyPressType pressType, IHotkeyTrigger trigger)
        {
            if (!scope.RegistrationsByAction.TryGetValue(action, out var actionRegistrations))
            {
                return false;
            }

            HotkeyRegistrationList registrations = actionRegistrations.GetRegistrations(pressType);
            if (!registrations.Remove(trigger, out var removedRegistration))
            {
                return false;
            }

            if (actionRegistrations.IsEmpty)
            {
                scope.RegistrationsByAction.Remove(action);
            }

            return true;
        }

        private static void RemoveActionRegistration(
            ActionRegistrationBucket bucket,
            EHotkeyPressType pressType,
            InputAction action,
            EHotkeyActionOwnershipMode ownershipMode)
        {
            AdjustBucketSubscription(bucket, pressType, ownershipMode, false);
            if (bucket.TotalCount == 0)
            {
                _actions.Remove(action);
            }
        }

        private static void AdjustBucketSubscription(
            ActionRegistrationBucket bucket,
            EHotkeyPressType pressType,
            EHotkeyActionOwnershipMode ownershipMode,
            bool add)
        {
            InputAction inputAction = bucket.Action;
            if (inputAction == null)
            {
                return;
            }

            int previousTotalCount = bucket.TotalCount;
            if (add && previousTotalCount == 0)
            {
                inputAction.started += _startedHandler;
                inputAction.canceled += _canceledHandler;
            }

            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    if (add)
                    {
                        bucket.StartedCount++;
                    }
                    else if (bucket.StartedCount > 0)
                    {
                        bucket.StartedCount--;
                    }

                    break;
                case EHotkeyPressType.Canceled:
                    if (add)
                    {
                        bucket.CanceledCount++;
                    }
                    else if (bucket.CanceledCount > 0)
                    {
                        bucket.CanceledCount--;
                    }

                    break;
                case EHotkeyPressType.Performed:
                default:
                    if (add)
                    {
                        if (bucket.PerformedCount == 0)
                        {
                            inputAction.performed += _performedHandler;
                        }

                        bucket.PerformedCount++;
                    }
                    else if (bucket.PerformedCount > 0)
                    {
                        bucket.PerformedCount--;
                        if (bucket.PerformedCount == 0)
                        {
                            inputAction.performed -= _performedHandler;
                        }
                    }

                    break;
            }

            if (!add && previousTotalCount > 0 && bucket.TotalCount == 0)
            {
                inputAction.started -= _startedHandler;
                inputAction.canceled -= _canceledHandler;
            }

            if (add)
            {
                AddManagedOwnership(bucket, ownershipMode);
            }
            else
            {
                RemoveManagedOwnership(bucket, ownershipMode);
            }
        }

        private static void AddManagedOwnership(ActionRegistrationBucket bucket, EHotkeyActionOwnershipMode ownershipMode)
        {
            if (ownershipMode != EHotkeyActionOwnershipMode.EnableWhileRegistered || bucket.Action == null)
            {
                return;
            }

            if (bucket.ManagedOwnershipCount == 0)
            {
                bucket.WasEnabledBeforeHotkey = bucket.Action.enabled;
                bucket.EnabledByHotkeySystem = false;
                if (!bucket.WasEnabledBeforeHotkey)
                {
                    bucket.Action.Enable();
                    bucket.EnabledByHotkeySystem = true;
                }
            }

            bucket.ManagedOwnershipCount++;
        }

        private static void RemoveManagedOwnership(ActionRegistrationBucket bucket, EHotkeyActionOwnershipMode ownershipMode)
        {
            if (ownershipMode != EHotkeyActionOwnershipMode.EnableWhileRegistered || bucket.Action == null || bucket.ManagedOwnershipCount <= 0)
            {
                return;
            }

            bucket.ManagedOwnershipCount--;
            if (bucket.ManagedOwnershipCount != 0)
            {
                return;
            }

            if (bucket.EnabledByHotkeySystem && !bucket.WasEnabledBeforeHotkey)
            {
                bucket.Action.Disable();
            }

            bucket.EnabledByHotkeySystem = false;
            bucket.WasEnabledBeforeHotkey = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Dispatch(InputAction.CallbackContext context, EHotkeyPressType pressType)
        {
            InputAction action = context.action;
            if (action == null)
            {
                return;
            }

            HotkeyPressTarget target;
            if (pressType == EHotkeyPressType.Started)
            {
                target = ResolveCurrentPressTarget();
                _pressTargets[action] = target;
            }
            else if (!_pressTargets.TryGetValue(action, out target))
            {
                if (pressType == EHotkeyPressType.Canceled)
                {
                    return;
                }

                target = ResolveCurrentPressTarget();
            }

            TryDispatchToLockedTarget(target, action, pressType);

            if (pressType == EHotkeyPressType.Canceled)
            {
                _pressTargets.Remove(action);
            }
        }

        private static HotkeyPressTarget ResolveCurrentPressTarget()
        {
            if (!TryGetCurrentHotkeyFocusHolder(out UIHolderObjectBase focusHolder))
            {
                return default;
            }

            EnsureScopeCacheCurrent();
            HotkeyScope leafScope = FindTopScopeInsideHolder(focusHolder);
            return new HotkeyPressTarget(focusHolder, leafScope);
        }

        private static bool TryGetCurrentHotkeyFocusHolder(out UIHolderObjectBase holder)
        {
            holder = null;
            if (!AppServices.TryGet(out IUIService uiService))
            {
                return false;
            }

            return uiService.TryGetTopVisibleHolder(_hotkeyFocusPredicate, out holder);
        }

        private static bool IsHotkeyFocusHolder(UIHolderObjectBase holder)
        {
            return holder != null && !holder.TryGetComponent<HotkeyPassThrough>(out _);
        }

        private static bool TryDispatchToLockedTarget(
            HotkeyPressTarget target,
            InputAction action,
            EHotkeyPressType pressType)
        {
            if (!target.HasFocus || !IsHolderAvailable(target.FocusHolder))
            {
                return false;
            }

            HotkeyScope leafScope = target.LeafScope;
            if (leafScope == null)
            {
                return false;
            }

            if (!IsDescendantOrSelf(leafScope.Holder, target.FocusHolder))
            {
                return false;
            }

            return TryDispatchToScopeChain(leafScope, target.FocusHolder, action, pressType);
        }

        private static void RemovePressTargetsForHolder(UIHolderObjectBase holder)
        {
            if (holder == null || _pressTargets.Count == 0)
            {
                return;
            }

            _pressTargetRemovalCount = 0;
            foreach (var pair in _pressTargets)
            {
                HotkeyPressTarget target = pair.Value;
                if (ReferenceEquals(target.FocusHolder, holder)
                    || IsDescendantOrSelf(target.FocusHolder, holder)
                    || target.LeafScope != null && IsDescendantOrSelf(target.LeafScope.Holder, holder))
                {
                    AddPressTargetRemoval(pair.Key);
                }
            }

            for (int i = 0; i < _pressTargetRemovalCount; i++)
            {
                _pressTargets.Remove(_pressTargetRemovalBuffer[i]);
                _pressTargetRemovalBuffer[i] = null;
            }

            _pressTargetRemovalCount = 0;
        }

        private static void AddPressTargetRemoval(InputAction action)
        {
            if (_pressTargetRemovalCount == _pressTargetRemovalBuffer.Length)
            {
                int newLength = _pressTargetRemovalBuffer.Length == 0 ? 4 : _pressTargetRemovalBuffer.Length << 1;
                Array.Resize(ref _pressTargetRemovalBuffer, newLength);
            }

            _pressTargetRemovalBuffer[_pressTargetRemovalCount++] = action;
        }

        private static bool TryDispatchToScopeChain(
            HotkeyScope leafScope,
            UIHolderObjectBase stopHolder,
            InputAction action,
            EHotkeyPressType pressType)
        {
            HotkeyScope current = leafScope;
            while (current != null)
            {
                if (IsScopeActive(current)
                    && TryDispatchRegistrations(current, action, pressType))
                {
                    return true;
                }

                if (ReferenceEquals(current.Holder, stopHolder))
                {
                    return false;
                }

                UIHolderObjectBase parentHolder = current.ParentHolder;
                current = parentHolder != null && _scopes.TryGetValue(parentHolder, out var parentScope)
                    ? parentScope
                    : null;
            }

            return false;
        }

        private static bool TryDispatchRegistrations(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType)
        {
            if (!scope.RegistrationsByAction.TryGetValue(action, out var actionRegistrations))
            {
                return false;
            }

            HotkeyRegistrationList registrations = actionRegistrations.GetRegistrations(pressType);
            for (int i = 0; i < registrations.Count; i++)
            {
                HotkeyRegistration registration = registrations[i];
                if (!IsTriggerAvailable(registration.Trigger))
                {
                    continue;
                }

                registration.Trigger.HotkeyActionTrigger();
                if (registration.ConsumesInput)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTriggerAvailable(IHotkeyTrigger trigger)
        {
            if (trigger == null)
            {
                return false;
            }

            if (trigger is Component component)
            {
                if (component == null || !component.gameObject.activeInHierarchy)
                {
                    return false;
                }

                if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureScopeCacheCurrent()
        {
            if (_scopeDirty)
            {
                RebuildScopeCacheImmediate();
            }
        }

        private static void RebuildScopeCacheImmediate()
        {
            for (int i = 0; i < _scopeList.Count; i++)
            {
                _scopeList[i].RefreshHierarchy();
            }

            _scopeDirty = false;
        }

        private static HotkeyScope FindTopScopeInsideHolder(UIHolderObjectBase focusHolder)
        {
            if (!IsHolderAvailable(focusHolder))
            {
                return null;
            }

            _ancestorHolders.Clear();

            for (int i = 0; i < _scopeList.Count; i++)
            {
                HotkeyScope scope = _scopeList[i];
                if (!IsScopeActive(scope) || !IsDescendantOrSelf(scope.Holder, focusHolder))
                {
                    continue;
                }

                UIHolderObjectBase parentHolder = scope.ParentHolder;
                while (parentHolder != null)
                {
                    _ancestorHolders.Add(parentHolder);
                    if (_scopes.TryGetValue(parentHolder, out var parentScope))
                    {
                        parentHolder = parentScope.ParentHolder;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            HotkeyScope bestScope = null;
            for (int i = 0; i < _scopeList.Count; i++)
            {
                HotkeyScope scope = _scopeList[i];
                if (IsScopeActive(scope)
                    && IsDescendantOrSelf(scope.Holder, focusHolder)
                    && !_ancestorHolders.Contains(scope.Holder)
                    && (bestScope == null || CompareScopePriority(scope, bestScope) < 0))
                {
                    bestScope = scope;
                }
            }

            return bestScope;
        }

        private static bool IsHolderAvailable(UIHolderObjectBase holder)
        {
            if (holder == null || !holder.IsValid())
            {
                return false;
            }

            return holder.gameObject.activeInHierarchy;
        }

        private static bool IsDescendantOrSelf(UIHolderObjectBase holder, UIHolderObjectBase root)
        {
            if (holder == null || root == null)
            {
                return false;
            }

            Transform current = holder.transform;
            Transform rootTransform = root.transform;
            while (current != null)
            {
                if (ReferenceEquals(current, rootTransform))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static void MarkScopeDirty()
        {
            _scopeDirty = true;
        }

        private static bool IsScopeActive(HotkeyScope scope)
        {
            if (scope == null || !scope.LifecycleActive)
            {
                return false;
            }

            UIHolderObjectBase holder = scope.Holder;
            if (holder == null || !holder.IsValid())
            {
                return false;
            }

            if (!holder.gameObject.activeInHierarchy)
            {
                return false;
            }

            Canvas canvas = scope.Canvas;
            return canvas != null && canvas.gameObject.layer == UIComponent.UIShowLayer;
        }

        private static bool IsScopeVisible(HotkeyScope scope)
        {
            UIHolderObjectBase holder = scope.Holder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
            {
                return false;
            }

            Canvas canvas = scope.Canvas;
            return canvas != null && canvas.gameObject.layer == UIComponent.UIShowLayer;
        }

        private static int CompareScopePriority(HotkeyScope left, HotkeyScope right)
        {
            int leftDepth = left.Canvas != null ? left.Canvas.sortingOrder : int.MinValue;
            int rightDepth = right.Canvas != null ? right.Canvas.sortingOrder : int.MinValue;
            int depthCompare = rightDepth.CompareTo(leftDepth);
            if (depthCompare != 0)
            {
                return depthCompare;
            }

            int hierarchyCompare = right.HierarchyDepth.CompareTo(left.HierarchyDepth);
            if (hierarchyCompare != 0)
            {
                return hierarchyCompare;
            }

            return right.ActivationSerial.CompareTo(left.ActivationSerial);
        }

        private static void CollectTriggers(HotkeyRegistrationList registrations)
        {
            for (int i = 0; i < registrations.Count; i++)
            {
                AddDestroyScopeTrigger(registrations[i].Trigger);
            }
        }

        private static void AddDestroyScopeTrigger(IHotkeyTrigger trigger)
        {
            if (_destroyScopeTriggerCount == _destroyScopeTriggers.Length)
            {
                int newLength = _destroyScopeTriggers.Length == 0 ? 8 : _destroyScopeTriggers.Length << 1;
                Array.Resize(ref _destroyScopeTriggers, newLength);
            }

            _destroyScopeTriggers[_destroyScopeTriggerCount++] = trigger;
        }

#if UNITY_EDITOR
        private static void WarnIfObservingDisabledAction(
            IHotkeyTrigger trigger,
            InputAction action,
            EHotkeyActionOwnershipMode ownershipMode)
        {
            if (ownershipMode != EHotkeyActionOwnershipMode.ObserveOnly || action == null || action.enabled)
            {
                return;
            }

            string triggerName = GetTriggerGameObjectName(trigger);
            Log.Warning(
                $"{triggerName} observes disabled hotkey action {action.name}. The hotkey system will not enable it; make sure the owning input map is enabled externally.");
        }

        private static void WarnRegistrationConflict(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType,
            IHotkeyTrigger registeredTrigger,
            IHotkeyTrigger rejectedTrigger)
        {
            string actionName = action != null ? action.name : "<null>";
            string holderName = scope.Holder != null ? scope.Holder.name : "<null>";
            string registeredName = GetTriggerGameObjectName(registeredTrigger);
            string rejectedName = GetTriggerGameObjectName(rejectedTrigger);
            Log.Warning(
                $"{rejectedName} repeated hotkey registration for {actionName} on holder {holderName} ({pressType}). Existing registration on {registeredName} keeps working; duplicate registration is ignored.");
        }

        private static string GetTriggerGameObjectName(IHotkeyTrigger trigger)
        {
            if (trigger is Component component && component != null)
            {
                return component.gameObject.name;
            }

            return trigger != null ? trigger.ToString() : "<null>";
        }
#endif

#if UNITY_EDITOR
        public static string GetDebugInfo()
        {
            return $"Actions: {_actions.Count}, Triggers: {_triggerMap.Count}, Scopes: {_scopeList.Count}, Dirty: {_scopeDirty}";
        }
#endif
    }
}

namespace UnityEngine.UI
{
    public static class UXHotkeyExtension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BindHotKey(this IHotkeyTrigger trigger)
        {
            InputActionReference action = trigger?.HotkeyAction;
            if (action == null)
            {
                return;
            }

            UIHolderObjectBase holder = trigger.HotkeyHolder;
            if (holder == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Log.Warning("Hotkey trigger could not find a UIHolderObjectBase owner.");
#endif
                return;
            }

            UXHotkeyRegisterManager.RegisterHotkey(trigger, holder, action, trigger.HotkeyPressType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnBindHotKey(this IHotkeyTrigger trigger)
        {
            UXHotkeyRegisterManager.UnregisterHotkey(trigger);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BindHotKeyBatch(this IHotkeyTrigger[] triggers)
        {
            if (triggers == null)
            {
                return;
            }

            for (int i = 0; i < triggers.Length; i++)
            {
                triggers[i]?.BindHotKey();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnBindHotKeyBatch(this IHotkeyTrigger[] triggers)
        {
            if (triggers == null)
            {
                return;
            }

            for (int i = 0; i < triggers.Length; i++)
            {
                triggers[i]?.UnBindHotKey();
            }
        }
    }
}
#endif
