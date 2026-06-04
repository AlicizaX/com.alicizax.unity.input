#if INPUTSYSTEM_SUPPORT
using System;
using System.Runtime.CompilerServices;
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UnityEngine.UI
{
    public enum EHotkeyPressType : byte
    {
        Started = 0,
        Performed = 1
    }

    internal readonly struct HotkeyRegistration
    {
        public readonly IHotkeyTrigger Trigger;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HotkeyRegistration(IHotkeyTrigger trigger)
        {
            Trigger = trigger;
        }
    }

    internal sealed class HotkeyActionRegistrations
    {
        public readonly HotkeyRegistrationList StartedRegistrations = new();
        public readonly HotkeyRegistrationList PerformedRegistrations = new();

        public bool IsEmpty => StartedRegistrations.Count == 0 && PerformedRegistrations.Count == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public HotkeyRegistrationList GetRegistrations(EHotkeyPressType pressType)
        {
            return pressType == EHotkeyPressType.Started ? StartedRegistrations : PerformedRegistrations;
        }
    }

    internal sealed class HotkeyRegistrationList
    {
        private HotkeyRegistration[] _items = Array.Empty<HotkeyRegistration>();

        public int Count { get; private set; }

        public HotkeyRegistration this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public void Add(HotkeyRegistration registration)
        {
            if (Count == _items.Length)
            {
                int newLength = _items.Length == 0 ? 2 : _items.Length << 1;
                Array.Resize(ref _items, newLength);
            }

            _items[Count++] = registration;
        }

        public void RemoveLast()
        {
            Count--;
            _items[Count] = default;
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

    internal sealed class ReferenceMap<TKey, TValue> where TKey : class
    {
        private int[] _buckets = Array.Empty<int>();
        private int[] _next = Array.Empty<int>();
        private TKey[] _keys = Array.Empty<TKey>();
        private TValue[] _values = Array.Empty<TValue>();
        private int _slotCount;

        public int Count { get; private set; }
        public int SlotCount => _slotCount;

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (key != null && _buckets.Length > 0)
            {
                int bucket = GetBucket(key, _buckets.Length);
                for (int i = _buckets[bucket] - 1; i >= 0; i = _next[i])
                {
                    if (ReferenceEquals(_keys[i], key))
                    {
                        value = _values[i];
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            if (key == null)
            {
                return;
            }

            EnsureCapacity(_slotCount + 1);
            int bucket = GetBucket(key, _buckets.Length);
            for (int i = _buckets[bucket] - 1; i >= 0; i = _next[i])
            {
                if (ReferenceEquals(_keys[i], key))
                {
                    _values[i] = value;
                    return;
                }
            }

            int index = _slotCount++;
            _keys[index] = key;
            _values[index] = value;
            _next[index] = _buckets[bucket] - 1;
            _buckets[bucket] = index + 1;
            Count++;
        }

        public bool Remove(TKey key)
        {
            if (key == null || _buckets.Length == 0)
            {
                return false;
            }

            int bucket = GetBucket(key, _buckets.Length);
            int previous = -1;
            for (int i = _buckets[bucket] - 1; i >= 0; i = _next[i])
            {
                if (!ReferenceEquals(_keys[i], key))
                {
                    previous = i;
                    continue;
                }

                if (previous < 0)
                {
                    _buckets[bucket] = _next[i] + 1;
                }
                else
                {
                    _next[previous] = _next[i];
                }

                _keys[i] = null;
                _values[i] = default;
                _next[i] = -1;
                Count--;
                return true;
            }

            return false;
        }

        public void Clear()
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            Array.Clear(_next, 0, _next.Length);
            Array.Clear(_keys, 0, _keys.Length);
            Array.Clear(_values, 0, _values.Length);
            _slotCount = 0;
            Count = 0;
        }

        public bool TryGetValueAtSlot(int slot, out TValue value)
        {
            if ((uint)slot < (uint)_slotCount && _keys[slot] != null)
            {
                value = _values[slot];
                return true;
            }

            value = default;
            return false;
        }

        private void EnsureCapacity(int capacity)
        {
            if (_buckets.Length >= capacity)
            {
                return;
            }

            int newLength = _buckets.Length == 0 ? 8 : _buckets.Length << 1;
            while (newLength < capacity)
            {
                newLength <<= 1;
            }

            TKey[] oldKeys = _keys;
            TValue[] oldValues = _values;
            int oldSlotCount = _slotCount;

            _buckets = new int[newLength];
            _next = new int[newLength];
            _keys = new TKey[newLength];
            _values = new TValue[newLength];
            _slotCount = 0;
            Count = 0;

            for (int i = 0; i < oldSlotCount; i++)
            {
                if (oldKeys[i] != null)
                {
                    Set(oldKeys[i], oldValues[i]);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBucket(TKey key, int length)
        {
            return (RuntimeHelpers.GetHashCode(key) & int.MaxValue) & (length - 1);
        }
    }

    internal sealed class HotkeyScope
    {
        public HotkeyScope(UIHolderObjectBase holder)
        {
            Holder = holder;
            HierarchyDepth = GetHierarchyDepth(holder.transform);
            ParentHolder = UXHotkeyRegisterManager.FindParentHolder(holder);
        }

        public readonly UIHolderObjectBase Holder;
        public readonly UIHolderObjectBase ParentHolder;
        public readonly int HierarchyDepth;
        public readonly ReferenceMap<InputAction, HotkeyActionRegistrations> RegistrationsByAction = new();

        public bool LifecycleActive;
        public ulong ActivationSerial;

        private Canvas _canvas;

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
        public bool WasEnabledBeforeHotkey;
        public bool EnabledByHotkeySystem;

        public int TotalCount => StartedCount + PerformedCount;
    }

    internal readonly struct TriggerRegistration
    {
        public readonly InputAction Action;
        public readonly UIHolderObjectBase Holder;
        public readonly EHotkeyPressType PressType;
        public readonly int Index;
        public readonly IHotkeyTrigger Trigger;

        public TriggerRegistration(InputAction action, UIHolderObjectBase holder, EHotkeyPressType pressType, int index, IHotkeyTrigger trigger)
        {
            Action = action;
            Holder = holder;
            PressType = pressType;
            Index = index;
            Trigger = trigger;
        }
    }

    internal static class UXHotkeyRegisterManager
    {
        private static readonly ReferenceMap<InputAction, ActionRegistrationBucket> _actions = new();
        private static readonly ReferenceMap<IHotkeyTrigger, TriggerRegistration> _triggerMap = new();
        private static readonly ReferenceMap<UIHolderObjectBase, HotkeyScope> _scopes = new();
        private static readonly ReferenceMap<UIHolderObjectBase, UIHolderObjectBase> _ancestorHolders = new();
        private static IHotkeyTrigger[] _destroyScopeTriggers = Array.Empty<IHotkeyTrigger>();
        private static int _destroyScopeTriggerCount;
        private static readonly Action<InputAction.CallbackContext> _startedHandler = OnActionStarted;
        private static readonly Action<InputAction.CallbackContext> _performedHandler = OnActionPerformed;

        private static ulong _serialCounter;
        private static HotkeyScope _topLeafScope;
        private static bool _isDestroyingScope;

#if UNITY_EDITOR
        [UnityEditor.Callbacks.DidReloadScripts]
        internal static void ClearHotkeyRegistry()
        {
            IHotkeyTrigger[] triggers = new IHotkeyTrigger[_triggerMap.Count];
            int index = 0;
            for (int i = 0; i < _triggerMap.SlotCount; i++)
            {
                if (_triggerMap.TryGetValueAtSlot(i, out var registration))
                {
                    triggers[index++] = registration.Trigger;
                }
            }

            for (int i = 0; i < triggers.Length; i++)
            {
                UnregisterHotkey(triggers[i]);
            }

            _actions.Clear();
            _triggerMap.Clear();
            _scopes.Clear();
            _ancestorHolders.Clear();
            _destroyScopeTriggerCount = 0;
            _serialCounter = 0;
            _topLeafScope = null;
            _isDestroyingScope = false;
            RebuildTopLeafScopeImmediate();
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
            int index = AddScopeRegistration(scope, inputAction, trigger, pressType);
            AdjustBucketSubscription(bucket, pressType, true);

            if (scope.LifecycleActive)
            {
                scope.ActivationSerial = ++_serialCounter;
                RebuildTopLeafScopeImmediate();
            }

            _triggerMap.Set(trigger, new TriggerRegistration(inputAction, holder, pressType, index, trigger));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void UnregisterHotkey(IHotkeyTrigger trigger)
        {
            if (trigger == null || !_triggerMap.TryGetValue(trigger, out var triggerRegistration))
            {
                return;
            }

            if (!_scopes.TryGetValue(triggerRegistration.Holder, out var scope))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError("Hotkey registry is inconsistent: scope missing during unregister.");
#endif
                return;
            }

            if (!RemoveScopeRegistration(scope, triggerRegistration.Action, triggerRegistration.PressType, triggerRegistration.Index, trigger))
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError("Hotkey registry is inconsistent: trigger slot missing during unregister.");
#endif
                return;
            }

            _triggerMap.Remove(trigger);
            if (_actions.TryGetValue(triggerRegistration.Action, out var bucket))
            {
                RemoveActionRegistration(bucket, triggerRegistration.PressType, triggerRegistration.Action);
            }

            ReleaseScopeIfEmpty(scope);
        }

        internal static void ActivateScope(UIHolderObjectBase holder)
        {
            if (_scopes.TryGetValue(holder, out var scope))
            {
                scope.LifecycleActive = true;
                scope.ActivationSerial = ++_serialCounter;
                RebuildTopLeafScopeImmediate();
            }
        }

        internal static void DeactivateScope(UIHolderObjectBase holder)
        {
            if (_scopes.TryGetValue(holder, out var scope))
            {
                scope.LifecycleActive = false;
                RebuildTopLeafScopeImmediate();
            }
        }

        internal static void DestroyScope(UIHolderObjectBase holder)
        {
            if (holder == null || _isDestroyingScope || !_scopes.TryGetValue(holder, out var scope))
            {
                return;
            }

            _isDestroyingScope = true;
            _destroyScopeTriggerCount = 0;
            for (int i = 0; i < scope.RegistrationsByAction.SlotCount; i++)
            {
                if (scope.RegistrationsByAction.TryGetValueAtSlot(i, out var actionRegistrations))
                {
                    CollectTriggers(actionRegistrations.StartedRegistrations);
                    CollectTriggers(actionRegistrations.PerformedRegistrations);
                }
            }

            for (int i = 0; i < _destroyScopeTriggerCount; i++)
            {
                UnregisterHotkey(_destroyScopeTriggers[i]);
            }

            Array.Clear(_destroyScopeTriggers, 0, _destroyScopeTriggerCount);
            _destroyScopeTriggerCount = 0;
            DetachScope(scope);
            _isDestroyingScope = false;
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
            Dispatch(context.action, EHotkeyPressType.Started);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void OnActionPerformed(InputAction.CallbackContext context)
        {
            Dispatch(context.action, EHotkeyPressType.Performed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ActionRegistrationBucket GetOrCreateBucket(InputAction inputAction)
        {
            if (_actions.TryGetValue(inputAction, out var bucket))
            {
                return bucket;
            }

            bucket = new ActionRegistrationBucket { Action = inputAction };
            _actions.Set(inputAction, bucket);
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
                ActivationSerial = ++_serialCounter
            };
            scope.LifecycleActive = IsScopeVisible(scope);

            holder.OnWindowBeforeShowEvent += scope.OnBeforeShowHandler;
            holder.OnWindowBeforeClosedEvent += scope.OnBeforeClosedHandler;
            holder.OnWindowDestroyEvent += scope.OnDestroyHandler;

            _scopes.Set(holder, scope);
            RebuildTopLeafScopeImmediate();
            return scope;
        }

        private static void DetachScope(HotkeyScope scope)
        {
            if (scope == null || scope.Holder == null)
            {
                return;
            }

            scope.Holder.OnWindowBeforeShowEvent -= scope.OnBeforeShowHandler;
            scope.Holder.OnWindowBeforeClosedEvent -= scope.OnBeforeClosedHandler;
            scope.Holder.OnWindowDestroyEvent -= scope.OnDestroyHandler;
            _scopes.Remove(scope.Holder);
            RebuildTopLeafScopeImmediate();
        }

        private static void ReleaseScopeIfEmpty(HotkeyScope scope)
        {
            if (scope != null && scope.RegistrationsByAction.Count == 0)
            {
                DetachScope(scope);
            }
        }

        private static int AddScopeRegistration(HotkeyScope scope, InputAction action, IHotkeyTrigger trigger, EHotkeyPressType pressType)
        {
            if (!scope.RegistrationsByAction.TryGetValue(action, out var actionRegistrations))
            {
                actionRegistrations = new HotkeyActionRegistrations();
                scope.RegistrationsByAction.Set(action, actionRegistrations);
            }

            HotkeyRegistrationList registrations = actionRegistrations.GetRegistrations(pressType);
            int index = registrations.Count;
            registrations.Add(new HotkeyRegistration(trigger));
            return index;
        }

        private static bool RemoveScopeRegistration(HotkeyScope scope, InputAction action, EHotkeyPressType pressType, int index, IHotkeyTrigger expectedTrigger)
        {
            if (!scope.RegistrationsByAction.TryGetValue(action, out var actionRegistrations))
            {
                return false;
            }

            HotkeyRegistrationList registrations = actionRegistrations.GetRegistrations(pressType);
            if ((uint)index >= (uint)registrations.Count)
            {
                return false;
            }

            int lastIndex = registrations.Count - 1;
            HotkeyRegistration removedRegistration = registrations[index];
            if (!ReferenceEquals(removedRegistration.Trigger, expectedTrigger))
            {
                return false;
            }

            if (index != lastIndex)
            {
                HotkeyRegistration movedRegistration = registrations[lastIndex];
                registrations[index] = movedRegistration;
                UpdateTriggerIndex(movedRegistration.Trigger, index);
            }

            registrations.RemoveLast();

            if (actionRegistrations.IsEmpty)
            {
                scope.RegistrationsByAction.Remove(action);
            }

            return true;
        }

        private static void UpdateTriggerIndex(IHotkeyTrigger trigger, int index)
        {
            if (trigger == null || !_triggerMap.TryGetValue(trigger, out var registration))
            {
                return;
            }

            _triggerMap.Set(trigger, new TriggerRegistration(registration.Action, registration.Holder, registration.PressType, index, trigger));
        }

        private static void RemoveActionRegistration(ActionRegistrationBucket bucket, EHotkeyPressType pressType, InputAction action)
        {
            AdjustBucketSubscription(bucket, pressType, false);
            if (bucket.TotalCount == 0)
            {
                _actions.Remove(action);
            }
        }

        private static void AdjustBucketSubscription(ActionRegistrationBucket bucket, EHotkeyPressType pressType, bool add)
        {
            InputAction inputAction = bucket.Action;
            if (inputAction == null)
            {
                return;
            }

            int countBefore = bucket.TotalCount;
            if (add && countBefore == 0)
            {
                bucket.WasEnabledBeforeHotkey = inputAction.enabled;
            }

            switch (pressType)
            {
                case EHotkeyPressType.Started:
                    if (add)
                    {
                        if (bucket.StartedCount == 0)
                        {
                            inputAction.started += _startedHandler;
                        }

                        bucket.StartedCount++;
                    }
                    else if (bucket.StartedCount > 0)
                    {
                        bucket.StartedCount--;
                        if (bucket.StartedCount == 0)
                        {
                            inputAction.started -= _startedHandler;
                        }
                    }

                    break;
                case EHotkeyPressType.Performed:
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

            if (add && countBefore == 0 && !bucket.WasEnabledBeforeHotkey)
            {
                inputAction.Enable();
                bucket.EnabledByHotkeySystem = true;
            }
            else if (!add && bucket.TotalCount == 0 && bucket.EnabledByHotkeySystem && !bucket.WasEnabledBeforeHotkey)
            {
                inputAction.Disable();
                bucket.EnabledByHotkeySystem = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Dispatch(InputAction action, EHotkeyPressType pressType)
        {
            RebuildTopLeafScopeImmediate();
            HotkeyScope leafScope = _topLeafScope;
            if (leafScope == null)
            {
                return;
            }

            TryDispatchToScopeChain(leafScope, action, pressType);
        }

        private static bool TryDispatchToScopeChain(HotkeyScope leafScope, InputAction action, EHotkeyPressType pressType)
        {
            HotkeyScope current = leafScope;
            while (current != null)
            {
                if (TryGetLatestRegistration(current, action, pressType, out var registration))
                {
                    registration.Trigger?.HotkeyActionTrigger();
                    return true;
                }

                UIHolderObjectBase parentHolder = current.ParentHolder;
                current = parentHolder != null && _scopes.TryGetValue(parentHolder, out var parentScope)
                    ? parentScope
                    : null;
            }

            return false;
        }

        private static bool TryGetLatestRegistration(HotkeyScope scope, InputAction action, EHotkeyPressType pressType, out HotkeyRegistration registration)
        {
            if (scope.RegistrationsByAction.TryGetValue(action, out var actionRegistrations))
            {
                HotkeyRegistrationList registrations = actionRegistrations.GetRegistrations(pressType);
                if (registrations.Count > 0)
                {
                    registration = registrations[registrations.Count - 1];
                    return registration.Trigger != null;
                }
            }

            registration = default;
            return false;
        }

        private static void RebuildTopLeafScopeImmediate()
        {
            _topLeafScope = RebuildTopLeafScope();
        }

        private static HotkeyScope RebuildTopLeafScope()
        {
            _ancestorHolders.Clear();

            for (int i = 0; i < _scopes.SlotCount; i++)
            {
                if (!_scopes.TryGetValueAtSlot(i, out var scope))
                {
                    continue;
                }

                if (!IsScopeActive(scope))
                {
                    continue;
                }

                UIHolderObjectBase parentHolder = scope.ParentHolder;
                while (parentHolder != null)
                {
                    _ancestorHolders.Set(parentHolder, parentHolder);
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
            for (int i = 0; i < _scopes.SlotCount; i++)
            {
                if (!_scopes.TryGetValueAtSlot(i, out var scope))
                {
                    continue;
                }

                if (IsScopeActive(scope)
                    && !_ancestorHolders.TryGetValue(scope.Holder, out _)
                    && (bestScope == null || CompareScopePriority(scope, bestScope) < 0))
                {
                    bestScope = scope;
                }
            }

            return bestScope;
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
        public static string GetDebugInfo()
        {
            return $"Actions: {_actions.Count}, Triggers: {_triggerMap.Count}, Scopes: {_scopes.Count}";
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
            if (trigger?.HotkeyAction == null)
            {
                return;
            }

            UIHolderObjectBase holder = trigger.HotkeyHolder;
            if (holder == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning("Hotkey trigger could not find a UIHolderObjectBase owner.");
#endif
                return;
            }

            UXHotkeyRegisterManager.RegisterHotkey(trigger, holder, trigger.HotkeyAction, trigger.HotkeyPressType);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UnBindHotKey(this IHotkeyTrigger trigger)
        {
            if (trigger?.HotkeyAction != null)
            {
                UXHotkeyRegisterManager.UnregisterHotkey(trigger);
            }
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
