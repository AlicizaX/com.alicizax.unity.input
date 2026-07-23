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
        public static readonly ReferenceEqualityComparer<TKey> Instance = new();

        private ReferenceEqualityComparer()
        {
        }

        public bool Equals(TKey x, TKey y) => ReferenceEquals(x, y);

        public int GetHashCode(TKey obj) => obj != null ? RuntimeHelpers.GetHashCode(obj) : 0;
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

    // One registration slot per press type (duplicate register is rejected).
    internal sealed class HotkeyActionRegistrations
    {
        private HotkeyRegistration _started;
        private HotkeyRegistration _performed;
        private HotkeyRegistration _canceled;
        private bool _hasStarted;
        private bool _hasPerformed;
        private bool _hasCanceled;

        public bool IsEmpty => !_hasStarted && !_hasPerformed && !_hasCanceled;

        public bool TryGet(EHotkeyPressType pressType, out HotkeyRegistration registration)
        {
            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    registration = _started;
                    return _hasStarted;
                case EHotkeyPressType.Canceled:
                    registration = _canceled;
                    return _hasCanceled;
                default:
                    registration = _performed;
                    return _hasPerformed;
            }
        }

        public bool TrySet(EHotkeyPressType pressType, HotkeyRegistration registration, out HotkeyComponentBase existingTrigger)
        {
            if (TryGet(pressType, out HotkeyRegistration existing))
            {
                existingTrigger = existing.Trigger;
                return false;
            }

            existingTrigger = null;
            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    _started = registration;
                    _hasStarted = true;
                    break;
                case EHotkeyPressType.Canceled:
                    _canceled = registration;
                    _hasCanceled = true;
                    break;
                default:
                    _performed = registration;
                    _hasPerformed = true;
                    break;
            }

            return true;
        }

        public bool TryClear(EHotkeyPressType pressType, HotkeyComponentBase trigger)
        {
            if (!TryGet(pressType, out HotkeyRegistration existing) || !ReferenceEquals(existing.Trigger, trigger))
            {
                return false;
            }

            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    _started = default;
                    _hasStarted = false;
                    break;
                case EHotkeyPressType.Canceled:
                    _canceled = default;
                    _hasCanceled = false;
                    break;
                default:
                    _performed = default;
                    _hasPerformed = false;
                    break;
            }

            return true;
        }

        public void CollectTriggers(List<HotkeyComponentBase> buffer)
        {
            if (_hasStarted)
            {
                buffer.Add(_started.Trigger);
            }

            if (_hasPerformed)
            {
                buffer.Add(_performed.Trigger);
            }

            if (_hasCanceled)
            {
                buffer.Add(_canceled.Trigger);
            }
        }
    }

    internal sealed class HotkeyScope
    {
        private Canvas _ownCanvas;
        private Canvas _displayCanvas;
        private bool _displayCanvasResolved;
        private bool _missingDisplayCanvasWarned;

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

        // Own Canvas if present; otherwise nearest parent Canvas (widgets).
        public Canvas DisplayCanvas
        {
            get
            {
                if (!_displayCanvasResolved)
                {
                    ResolveDisplayCanvas();
                }
                // Unity destroyed a previously cached component (fake-null).
                else if ((object)_displayCanvas != null && _displayCanvas == null)
                {
                    _displayCanvasResolved = false;
                    ResolveDisplayCanvas();
                }

                return _displayCanvas;
            }
        }

        public void RefreshHierarchy()
        {
            if (Holder == null)
            {
                ParentHolder = null;
                HierarchyDepth = 0;
                InvalidateDisplayCanvas();
                return;
            }

            HierarchyDepth = GetHierarchyDepth(Holder.transform);
            ParentHolder = UXHotkeySystem.FindParentHolder(Holder);
            InvalidateDisplayCanvas();
        }

        private void InvalidateDisplayCanvas()
        {
            _displayCanvasResolved = false;
            _displayCanvas = null;
        }

        private void ResolveDisplayCanvas()
        {
            _displayCanvasResolved = true;
            _displayCanvas = null;

            if (Holder == null)
            {
                return;
            }

            if (_ownCanvas == null)
            {
                _ownCanvas = Holder.GetComponent<Canvas>();
            }

            if (_ownCanvas != null)
            {
                _displayCanvas = _ownCanvas;
                return;
            }

            _displayCanvas = Holder.GetComponentInParent<Canvas>(true);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Parent fallback is normal for widgets. Only warn when nothing is found.
            if (_displayCanvas == null && !_missingDisplayCanvasWarned)
            {
                _missingDisplayCanvasWarned = true;
                string holderName = Holder != null ? Holder.name : "<null>";
                Log.Warning(
                    $"Hotkey scope on holder '{holderName}' has no Canvas in parents. " +
                    "Visibility falls back to lifecycle active + activeInHierarchy; sorting priority is reduced.");
            }
#endif
        }

        public void OnBeforeShowHandler() => UXHotkeySystem.ActivateScope(Holder);

        public void OnBeforeClosedHandler() => UXHotkeySystem.DeactivateScope(Holder);

        public void OnDestroyHandler() => UXHotkeySystem.DestroyScope(Holder);

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

        public int TotalCount => StartedCount + PerformedCount + CanceledCount;
    }

    internal readonly struct TriggerRegistration
    {
        public readonly InputAction Action;
        public readonly UIHolderObjectBase Holder;
        public readonly EHotkeyPressType PressType;

        public TriggerRegistration(InputAction action, UIHolderObjectBase holder, EHotkeyPressType pressType)
        {
            Action = action;
            Holder = holder;
            PressType = pressType;
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

    internal static class UXHotkeySystem
    {
        private static readonly Dictionary<InputAction, ActionRegistrationBucket> _actions =
            new(ReferenceEqualityComparer<InputAction>.Instance);

        private static readonly Dictionary<InputAction, HotkeyPressTarget> _pressTargets =
            new(ReferenceEqualityComparer<InputAction>.Instance);

        private static readonly Dictionary<HotkeyComponentBase, TriggerRegistration> _triggerMap =
            new(ReferenceEqualityComparer<HotkeyComponentBase>.Instance);

        private static readonly Dictionary<UIHolderObjectBase, HotkeyScope> _scopes =
            new(ReferenceEqualityComparer<UIHolderObjectBase>.Instance);

        private static readonly List<HotkeyScope> _scopeList = new(32);
        private static readonly HashSet<UIHolderObjectBase> _ancestorHolders =
            new(ReferenceEqualityComparer<UIHolderObjectBase>.Instance);

        private static readonly List<HotkeyComponentBase> _destroyScopeTriggers = new(16);
        private static readonly List<InputAction> _pressTargetRemovalBuffer = new(8);

        private static readonly Action<InputAction.CallbackContext> _startedHandler = OnActionStarted;
        private static readonly Action<InputAction.CallbackContext> _performedHandler = OnActionPerformed;
        private static readonly Action<InputAction.CallbackContext> _canceledHandler = OnActionCanceled;
        private static readonly Predicate<UIHolderObjectBase> _hotkeyFocusPredicate = IsHotkeyFocusHolder;

        private static ulong _activationSerial;
        private static bool _hierarchyDirty = true;
        private static bool _isDestroyingScope;
        private static HotkeyAppHookRunner _appHookRunner;

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        internal static void ClearHotkeyRegistry()
        {
            HotkeyComponentBase[] triggers = new HotkeyComponentBase[_triggerMap.Count];
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
            _destroyScopeTriggers.Clear();
            _pressTargetRemovalBuffer.Clear();
            _activationSerial = 0;
            _isDestroyingScope = false;
            _hierarchyDirty = true;
            DestroyAppHooks();
            RebuildHierarchyIfDirty();
        }
#endif

        internal static void ResetTransientState()
        {
            _pressTargets.Clear();
            _pressTargetRemovalBuffer.Clear();
        }

        private static void EnsureAppHooks()
        {
            if (_appHookRunner != null)
            {
                return;
            }

            var go = new GameObject("[UXHotkeySystem]");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _appHookRunner = go.AddComponent<HotkeyAppHookRunner>();
        }

        private static void DestroyAppHooks()
        {
            if (_appHookRunner == null)
            {
                return;
            }

            Object.DestroyImmediate(_appHookRunner.gameObject);
            _appHookRunner = null;
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

        internal static void RegisterHotkey(
            HotkeyComponentBase trigger,
            UIHolderObjectBase holder,
            InputActionReference action,
            EHotkeyPressType pressType)
        {
            if (trigger == null || holder == null || action == null || action.action == null)
            {
                return;
            }

            EnsureAppHooks();
            UnregisterHotkey(trigger);

            InputAction inputAction = action.action;
            HotkeyScope scope = GetOrCreateScope(holder);
            // Reparent can happen without scope recreate; refresh only this scope.
            scope.RefreshHierarchy();

#if UNITY_EDITOR
            WarnIfObservingDisabledAction(trigger, inputAction);
#endif
            HotkeyRegistration registration = new(trigger, trigger.HotkeyConsumesInput);
            if (!TryAddScopeRegistration(scope, inputAction, pressType, registration))
            {
                ReleaseScopeIfEmpty(scope);
                return;
            }

            ActionRegistrationBucket bucket = GetOrCreateBucket(inputAction);
            AdjustBucketSubscription(bucket, pressType, true);

            if (scope.LifecycleActive)
            {
                scope.ActivationSerial = ++_activationSerial;
            }

            _triggerMap[trigger] = new TriggerRegistration(inputAction, holder, pressType);
        }

        internal static void UnregisterHotkey(HotkeyComponentBase trigger)
        {
            if (trigger == null || !_triggerMap.TryGetValue(trigger, out var triggerRegistration))
            {
                return;
            }

            HotkeyScope scope = null;
            bool removedFromScope = false;
            if (_scopes.TryGetValue(triggerRegistration.Holder, out scope))
            {
                removedFromScope = RemoveScopeRegistration(
                    scope,
                    triggerRegistration.Action,
                    triggerRegistration.PressType,
                    trigger);
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
                RemoveActionRegistration(bucket, triggerRegistration.PressType, triggerRegistration.Action);
            }

            ReleaseScopeIfEmpty(scope);
        }

        internal static void ActivateScope(UIHolderObjectBase holder)
        {
            if (!_scopes.TryGetValue(holder, out var scope))
            {
                return;
            }

            scope.LifecycleActive = true;
            scope.ActivationSerial = ++_activationSerial;
            // Activation does not change hierarchy.
        }

        internal static void DeactivateScope(UIHolderObjectBase holder)
        {
            if (!_scopes.TryGetValue(holder, out var scope))
            {
                return;
            }

            scope.LifecycleActive = false;
            RemovePressTargetsForHolder(holder);
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
                _destroyScopeTriggers.Clear();
                foreach (var pair in scope.RegistrationsByAction)
                {
                    pair.Value.CollectTriggers(_destroyScopeTriggers);
                }

                for (int i = 0; i < _destroyScopeTriggers.Count; i++)
                {
                    UnregisterHotkey(_destroyScopeTriggers[i]);
                }

                _destroyScopeTriggers.Clear();

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

        private static void OnActionStarted(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Started);

        private static void OnActionPerformed(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Performed);

        private static void OnActionCanceled(InputAction.CallbackContext context) =>
            Dispatch(context, EHotkeyPressType.Canceled);

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
            MarkHierarchyDirty();
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
            MarkHierarchyDirty();
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

            if (!actionRegistrations.TrySet(pressType, registration, out HotkeyComponentBase existingTrigger))
            {
#if UNITY_EDITOR
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
            if (!scope.RegistrationsByAction.TryGetValue(action, out var actionRegistrations))
            {
                return false;
            }

            if (!actionRegistrations.TryClear(pressType, trigger))
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
            InputAction action)
        {
            AdjustBucketSubscription(bucket, pressType, false);
            if (bucket.TotalCount == 0)
            {
                _actions.Remove(action);
            }
        }

        private static void AdjustBucketSubscription(
            ActionRegistrationBucket bucket,
            EHotkeyPressType pressType,
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
        }

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

            RebuildHierarchyIfDirty();
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

            _pressTargetRemovalBuffer.Clear();
            foreach (var pair in _pressTargets)
            {
                HotkeyPressTarget target = pair.Value;
                if (ReferenceEquals(target.FocusHolder, holder)
                    || IsDescendantOrSelf(target.FocusHolder, holder)
                    || target.LeafScope != null && IsDescendantOrSelf(target.LeafScope.Holder, holder))
                {
                    _pressTargetRemovalBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < _pressTargetRemovalBuffer.Count; i++)
            {
                _pressTargets.Remove(_pressTargetRemovalBuffer[i]);
            }

            _pressTargetRemovalBuffer.Clear();
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
                if (IsScopeActive(current) && TryDispatchRegistration(current, action, pressType))
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

        private static bool TryDispatchRegistration(
            HotkeyScope scope,
            InputAction action,
            EHotkeyPressType pressType)
        {
            if (!scope.RegistrationsByAction.TryGetValue(action, out var actionRegistrations))
            {
                return false;
            }

            if (!actionRegistrations.TryGet(pressType, out HotkeyRegistration registration))
            {
                return false;
            }

            if (!IsTriggerAvailable(registration.Trigger))
            {
                return false;
            }

            registration.Trigger.HotkeyActionTrigger();
            return registration.ConsumesInput;
        }

        private static bool IsTriggerAvailable(HotkeyComponentBase trigger)
        {
            return trigger != null && trigger.isActiveAndEnabled;
        }

        private static void RebuildHierarchyIfDirty()
        {
            if (!_hierarchyDirty)
            {
                return;
            }

            for (int i = 0; i < _scopeList.Count; i++)
            {
                _scopeList[i].RefreshHierarchy();
            }

            _hierarchyDirty = false;
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
            return holder != null && holder.IsValid() && holder.gameObject.activeInHierarchy;
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

        private static void MarkHierarchyDirty()
        {
            _hierarchyDirty = true;
        }

        private static bool IsScopeActive(HotkeyScope scope)
        {
            if (scope == null || !scope.LifecycleActive)
            {
                return false;
            }

            UIHolderObjectBase holder = scope.Holder;
            if (holder == null || !holder.IsValid() || !holder.gameObject.activeInHierarchy)
            {
                return false;
            }

            return IsDisplayCanvasShownOrFallback(scope.DisplayCanvas);
        }

        private static bool IsScopeVisible(HotkeyScope scope)
        {
            UIHolderObjectBase holder = scope.Holder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
            {
                return false;
            }

            return IsDisplayCanvasShownOrFallback(scope.DisplayCanvas);
        }

        // No Canvas at all: allow (lifecycle + activeInHierarchy already checked).
        private static bool IsDisplayCanvasShownOrFallback(Canvas displayCanvas)
        {
            if (displayCanvas == null)
            {
                return true;
            }

            return displayCanvas.gameObject.layer == UIComponent.UIShowLayer;
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

#if UNITY_EDITOR
        private static void WarnIfObservingDisabledAction(HotkeyComponentBase trigger, InputAction action)
        {
            if (action == null || action.enabled)
            {
                return;
            }

            string triggerName = GetTriggerGameObjectName(trigger);
            Log.Warning(
                $"{triggerName} observes disabled hotkey action {action.name}. " +
                "The hotkey system will not enable it; make sure the owning input map is enabled externally.");
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
            string registeredName = GetTriggerGameObjectName(registeredTrigger);
            string rejectedName = GetTriggerGameObjectName(rejectedTrigger);
            Log.Warning(
                $"{rejectedName} repeated hotkey registration for {actionName} on holder {holderName} ({pressType}). "
                + $"Existing registration on {registeredName} keeps working; duplicate registration is ignored. "
                + "Disable the previous widget or component before registering another hotkey for the same holder, action, and press type.");
        }

        private static string GetTriggerGameObjectName(HotkeyComponentBase trigger)
        {
            return trigger != null ? trigger.gameObject.name : "<null>";
        }

        public static string GetDebugInfo()
        {
            return $"Actions: {_actions.Count}, Triggers: {_triggerMap.Count}, Scopes: {_scopeList.Count}, HierarchyDirty: {_hierarchyDirty}";
        }
#endif
    }
}

namespace UnityEngine.UI
{
    public static class UXHotkeyExtension
    {
        public static void BindHotKey(this HotkeyComponentBase trigger)
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

            UXHotkeySystem.RegisterHotkey(trigger, holder, action, trigger.HotkeyPressType);
        }

        public static void UnBindHotKey(this HotkeyComponentBase trigger)
        {
            UXHotkeySystem.UnregisterHotkey(trigger);
        }

        public static void BindHotKeyBatch(this HotkeyComponentBase[] triggers)
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

        public static void UnBindHotKeyBatch(this HotkeyComponentBase[] triggers)
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
