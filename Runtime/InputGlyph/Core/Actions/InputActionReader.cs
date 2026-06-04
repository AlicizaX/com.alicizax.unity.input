#if INPUTSYSTEM_SUPPORT
using System;
using System.Runtime.CompilerServices;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

public static class InputActionReader
{
    private const int InitialStateCapacity = 64;

    private static readonly InputReadStateMap PressedKeys = new InputReadStateMap(InitialStateCapacity);
    private static readonly InputReadStateMap ToggledKeys = new InputReadStateMap(InitialStateCapacity);
    private static readonly CompositePartControlMap CompositePartControls = new CompositePartControlMap(32);

    public static InputAction ResolveAction(string actionName)
    {
        return string.IsNullOrWhiteSpace(actionName) ? null : InputActionResolver.Action(actionName);
    }

    public static void ClearBindingCaches()
    {
        CompositePartControls.Clear();
    }

    public static T ReadValue<T>(InputAction action) where T : struct
    {
        return action != null ? action.ReadValue<T>() : default;
    }

    public static T ReadValue<T>(string actionName) where T : struct
    {
        return ReadValue<T>(ResolveAction(actionName));
    }

    public static bool TryReadValue<T>(InputAction action, out T value) where T : struct
    {
        if (action != null && action.IsPressed())
        {
            value = action.ReadValue<T>();
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryReadValue<T>(string actionName, out T value) where T : struct
    {
        return TryReadValue(ResolveAction(actionName), out value);
    }

    public static bool TryReadValueOnce<T>(Object owner, InputAction action, out T value) where T : struct
    {
        if (owner == null)
        {
            value = default;
            return false;
        }

        return TryReadValueOnceInternal(action, null, null, owner.GetInstanceID(), null, out value);
    }

    public static bool TryReadValueOnce<T>(Object owner, string actionName, out T value) where T : struct
    {
        if (owner == null)
        {
            value = default;
            return false;
        }

        InputAction action = ResolveAction(actionName);
        return TryReadValueOnceInternal(action, actionName, null, owner.GetInstanceID(), null, out value);
    }

    public static bool ReadButton(InputAction action)
    {
        return action != null && action.type == InputActionType.Button && action.IsPressed();
    }

    public static bool ReadButton(string actionName)
    {
        return ReadButton(ResolveAction(actionName));
    }

    public static bool ReadPressed(InputAction action)
    {
        return action != null && action.IsPressed();
    }

    public static bool ReadPressed(string actionName)
    {
        return ReadPressed(ResolveAction(actionName));
    }

    public static bool ReadPressedOnce(Object owner, InputAction action)
    {
        return owner != null && ReadPressedOnce(owner.GetInstanceID(), action);
    }

    public static bool ReadPressedOnce(Object owner, string actionName)
    {
        return owner != null && ReadPressedOnce(owner.GetInstanceID(), actionName);
    }

    public static bool ReadPressedOnce(int ownerId, InputAction action)
    {
        return ReadButtonOnceInternal(action, null, null, ownerId, null, ReadPressed(action));
    }

    public static bool ReadPressedOnce(int ownerId, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(action, actionName, null, ownerId, null, ReadPressed(action));
    }

    public static bool ReadPressedOnce(string key, InputAction action)
    {
        return ReadButtonOnceInternal(action, null, null, 0, key, ReadPressed(action));
    }

    public static bool ReadPressedOnce(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(action, actionName, null, 0, key, ReadPressed(action));
    }

    public static bool ReadPressedToggle(Object owner, InputAction action)
    {
        return owner != null && ReadPressedToggle(owner.GetInstanceID(), action);
    }

    public static bool ReadPressedToggle(Object owner, string actionName)
    {
        return owner != null && ReadPressedToggle(owner.GetInstanceID(), actionName);
    }

    public static bool ReadPressedToggle(int ownerId, InputAction action)
    {
        return ReadButtonToggleInternal(action, null, null, ownerId, null, ReadPressed(action));
    }

    public static bool ReadPressedToggle(int ownerId, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(action, actionName, null, ownerId, null, ReadPressed(action));
    }

    public static bool ReadPressedToggle(string key, InputAction action)
    {
        return ReadButtonToggleInternal(action, null, null, 0, key, ReadPressed(action));
    }

    public static bool ReadPressedToggle(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(action, actionName, null, 0, key, ReadPressed(action));
    }

    public static bool ReadButtonOnce(Object owner, InputAction action)
    {
        return owner != null && ReadButtonOnce(owner.GetInstanceID(), action);
    }

    public static bool ReadButtonOnce(Object owner, string actionName)
    {
        return owner != null && ReadButtonOnce(owner.GetInstanceID(), actionName);
    }

    public static bool ReadButtonOnce(int ownerId, InputAction action)
    {
        return ReadButtonOnceInternal(action, null, null, ownerId, null, ReadButton(action));
    }

    public static bool ReadButtonOnce(int ownerId, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(action, actionName, null, ownerId, null, ReadButton(action));
    }

    public static bool ReadButtonOnce(string key, InputAction action)
    {
        return ReadButtonOnceInternal(action, null, null, 0, key, ReadButton(action));
    }

    public static bool ReadButtonOnce(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(action, actionName, null, 0, key, ReadButton(action));
    }

    public static bool ReadButtonToggle(Object owner, InputAction action)
    {
        return owner != null && ReadButtonToggle(owner.GetInstanceID(), action);
    }

    public static bool ReadButtonToggle(Object owner, string actionName)
    {
        return owner != null && ReadButtonToggle(owner.GetInstanceID(), actionName);
    }

    public static bool ReadButtonToggle(int ownerId, InputAction action)
    {
        return ReadButtonToggleInternal(action, null, null, ownerId, null, ReadButton(action));
    }

    public static bool ReadButtonToggle(int ownerId, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(action, actionName, null, ownerId, null, ReadButton(action));
    }

    public static bool ReadButtonToggle(string key, InputAction action)
    {
        return ReadButtonToggleInternal(action, null, null, 0, key, ReadButton(action));
    }

    public static bool ReadButtonToggle(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(action, actionName, null, 0, key, ReadButton(action));
    }

    public static bool ReadCompositePartButton(InputAction action, string compositePartName)
    {
        if (action == null || !action.enabled || string.IsNullOrWhiteSpace(compositePartName))
        {
            return false;
        }

        if (CompositePartControls.TryGet(action, compositePartName, out InputControl cachedControl))
        {
            return cachedControl != null && cachedControl.IsPressed();
        }

        InputControl control = ResolveCompositePartControl(action, compositePartName);
        CompositePartControls.Set(action, compositePartName, control);
        return control != null && control.IsPressed();
    }

    public static bool ReadCompositePartButton(string actionName, string compositePartName)
    {
        return ReadCompositePartButton(ResolveAction(actionName), compositePartName);
    }

    public static bool ReadCompositePartButtonOnce(Object owner, InputAction action, string compositePartName)
    {
        return owner != null && ReadCompositePartButtonOnce(owner.GetInstanceID(), action, compositePartName);
    }

    public static bool ReadCompositePartButtonOnce(Object owner, string actionName, string compositePartName)
    {
        return owner != null && ReadCompositePartButtonOnce(owner.GetInstanceID(), actionName, compositePartName);
    }

    public static bool ReadCompositePartButtonOnce(int ownerId, InputAction action, string compositePartName)
    {
        return ReadButtonOnceInternal(
            action,
            null,
            compositePartName,
            ownerId,
            null,
            ReadCompositePartButton(action, compositePartName));
    }

    public static bool ReadCompositePartButtonOnce(int ownerId, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(
            action,
            actionName,
            compositePartName,
            ownerId,
            null,
            ReadCompositePartButton(action, compositePartName));
    }

    public static bool ReadCompositePartButtonOnce(string key, InputAction action, string compositePartName)
    {
        return ReadButtonOnceInternal(
            action,
            null,
            compositePartName,
            0,
            key,
            ReadCompositePartButton(action, compositePartName));
    }

    public static bool ReadCompositePartButtonOnce(string key, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(
            action,
            actionName,
            compositePartName,
            0,
            key,
            ReadCompositePartButton(action, compositePartName));
    }

    public static bool ReadCompositePartButtonToggle(Object owner, InputAction action, string compositePartName)
    {
        return owner != null && ReadCompositePartButtonToggle(owner.GetInstanceID(), action, compositePartName);
    }

    public static bool ReadCompositePartButtonToggle(Object owner, string actionName, string compositePartName)
    {
        return owner != null && ReadCompositePartButtonToggle(owner.GetInstanceID(), actionName, compositePartName);
    }

    public static bool ReadCompositePartButtonToggle(int ownerId, InputAction action, string compositePartName)
    {
        return ReadButtonToggleInternal(
            action,
            null,
            compositePartName,
            ownerId,
            null,
            ReadCompositePartButton(action, compositePartName));
    }

    public static bool ReadCompositePartButtonToggle(int ownerId, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(
            action,
            actionName,
            compositePartName,
            ownerId,
            null,
            ReadCompositePartButton(action, compositePartName));
    }

    public static bool ReadCompositePartButtonToggle(string key, InputAction action, string compositePartName)
    {
        return ReadButtonToggleInternal(
            action,
            null,
            compositePartName,
            0,
            key,
            ReadCompositePartButton(action, compositePartName));
    }

    public static bool ReadCompositePartButtonToggle(string key, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(
            action,
            actionName,
            compositePartName,
            0,
            key,
            ReadCompositePartButton(action, compositePartName));
    }

    public static void ResetToggledButton(string key, InputAction action)
    {
        Guid actionId = GetActionId(action);
        ToggledKeys.Remove(in actionId, string.Empty, string.Empty, 0, key);
    }

    public static void ResetToggledButton(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        Guid actionId = GetActionId(action);
        string actionKey = action == null ? Normalize(actionName) : string.Empty;
        ToggledKeys.Remove(in actionId, actionKey, string.Empty, 0, key);
    }

    public static void ResetToggledCompositePartButton(string key, InputAction action, string compositePartName)
    {
        Guid actionId = GetActionId(action);
        ToggledKeys.Remove(in actionId, string.Empty, compositePartName, 0, key);
    }

    public static void ResetToggledCompositePartButton(string key, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        Guid actionId = GetActionId(action);
        string actionKey = action == null ? Normalize(actionName) : string.Empty;
        ToggledKeys.Remove(in actionId, actionKey, compositePartName, 0, key);
    }

    public static void ResetToggledButton(InputAction action)
    {
        if (action == null)
        {
            return;
        }

        Guid actionId = action.id;
        ToggledKeys.RemoveByAction(in actionId);
    }

    public static void ResetToggledButton(string actionName)
    {
        InputAction action = ResolveAction(actionName);
        if (action != null)
        {
            Guid actionId = action.id;
            ToggledKeys.RemoveByAction(in actionId);
            return;
        }

        ToggledKeys.RemoveByActionName(actionName);
    }

    public static void ResetToggledButtons()
    {
        ToggledKeys.Clear();
    }

    private static bool TryReadValueOnceInternal<T>(
        InputAction action,
        string actionName,
        string compositePartName,
        int ownerId,
        string ownerKey,
        out T value) where T : struct
    {
        Guid actionId = GetActionId(action);
        string actionKey = action != null ? string.Empty : Normalize(actionName);
        if (action != null && action.IsPressed())
        {
            if (PressedKeys.Add(in actionId, actionKey, compositePartName, ownerId, ownerKey))
            {
                value = action.ReadValue<T>();
                return true;
            }
        }
        else
        {
            PressedKeys.Remove(in actionId, actionKey, compositePartName, ownerId, ownerKey);
        }

        value = default;
        return false;
    }

    private static bool ReadButtonOnceInternal(
        InputAction action,
        string actionName,
        string compositePartName,
        int ownerId,
        string ownerKey,
        bool isPressed)
    {
        Guid actionId = GetActionId(action);
        string actionKey = action != null ? string.Empty : Normalize(actionName);
        if (isPressed)
        {
            return PressedKeys.Add(in actionId, actionKey, compositePartName, ownerId, ownerKey);
        }

        PressedKeys.Remove(in actionId, actionKey, compositePartName, ownerId, ownerKey);
        return false;
    }

    private static bool ReadButtonToggleInternal(
        InputAction action,
        string actionName,
        string compositePartName,
        int ownerId,
        string ownerKey,
        bool isPressed)
    {
        Guid actionId = GetActionId(action);
        string actionKey = action != null ? string.Empty : Normalize(actionName);
        if (ReadButtonOnceInternal(action, actionName, compositePartName, ownerId, ownerKey, isPressed))
        {
            if (!ToggledKeys.Add(in actionId, actionKey, compositePartName, ownerId, ownerKey))
            {
                ToggledKeys.Remove(in actionId, actionKey, compositePartName, ownerId, ownerKey);
            }
        }

        return ToggledKeys.Contains(in actionId, actionKey, compositePartName, ownerId, ownerKey);
    }

    private static InputControl ResolveCompositePartControl(InputAction action, string compositePartName)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (!IsCompositePart(binding, compositePartName))
            {
                continue;
            }

            string path = GetEffectivePath(binding);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var controls = action.controls;
            for (int c = 0; c < controls.Count; c++)
            {
                InputControl control = controls[c];
                if (InputControlPath.Matches(path, control))
                {
                    return control;
                }
            }
        }

        return null;
    }

    private static bool IsCompositePart(InputBinding binding, string compositePartName)
    {
        return binding.isPartOfComposite
               && InputGlyphStringUtility.EqualsIgnoreCase(binding.name, compositePartName);
    }

    private static string GetEffectivePath(InputBinding binding)
    {
        return string.IsNullOrWhiteSpace(binding.effectivePath) ? binding.path : binding.effectivePath;
    }

    private static Guid GetActionId(InputAction action)
    {
        return action != null ? action.id : Guid.Empty;
    }

    private static string Normalize(string value)
    {
        return value ?? string.Empty;
    }

    private sealed class InputReadStateMap
    {
        private Guid[] _actionIds;
        private string[] _actionNames;
        private string[] _compositePartNames;
        private int[] _ownerIds;
        private string[] _ownerKeys;
        private int[] _hashes;
        private bool[] _occupied;
        private int _mask;
        private int _count;

        public InputReadStateMap(int capacity)
        {
            int resolvedCapacity = NextPowerOfTwo(capacity);
            _actionIds = new Guid[resolvedCapacity];
            _actionNames = new string[resolvedCapacity];
            _compositePartNames = new string[resolvedCapacity];
            _ownerIds = new int[resolvedCapacity];
            _ownerKeys = new string[resolvedCapacity];
            _hashes = new int[resolvedCapacity];
            _occupied = new bool[resolvedCapacity];
            _mask = resolvedCapacity - 1;
        }

        public void Clear()
        {
            Array.Clear(_actionIds, 0, _actionIds.Length);
            Array.Clear(_actionNames, 0, _actionNames.Length);
            Array.Clear(_compositePartNames, 0, _compositePartNames.Length);
            Array.Clear(_ownerIds, 0, _ownerIds.Length);
            Array.Clear(_ownerKeys, 0, _ownerKeys.Length);
            Array.Clear(_hashes, 0, _hashes.Length);
            Array.Clear(_occupied, 0, _occupied.Length);
            _count = 0;
        }

        public bool Contains(
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            int hash = BuildHash(in actionId, actionName, compositePartName, ownerId, ownerKey);
            return FindSlot(hash, in actionId, actionName, compositePartName, ownerId, ownerKey) >= 0;
        }

        public bool Add(
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            if ((_count + 1) * 2 >= _occupied.Length)
            {
                Resize(_occupied.Length << 1);
            }

            int hash = BuildHash(in actionId, actionName, compositePartName, ownerId, ownerKey);
            if (FindSlot(hash, in actionId, actionName, compositePartName, ownerId, ownerKey) >= 0)
            {
                return false;
            }

            SetInternal(hash, in actionId, actionName, compositePartName, ownerId, ownerKey);
            return true;
        }

        public bool Remove(
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            int hash = BuildHash(in actionId, actionName, compositePartName, ownerId, ownerKey);
            int slot = FindSlot(hash, in actionId, actionName, compositePartName, ownerId, ownerKey);
            if (slot < 0)
            {
                return false;
            }

            RemoveAt(slot);
            return true;
        }

        public void RemoveByAction(in Guid actionId)
        {
            for (int i = 0; i < _occupied.Length;)
            {
                if (_occupied[i] && _actionIds[i].Equals(actionId))
                {
                    RemoveAt(i);
                    continue;
                }

                i++;
            }
        }

        public void RemoveByActionName(string actionName)
        {
            string normalizedActionName = Normalize(actionName);
            for (int i = 0; i < _occupied.Length;)
            {
                if (_occupied[i] && InputGlyphStringUtility.EqualsOrdinal(_actionNames[i], normalizedActionName))
                {
                    RemoveAt(i);
                    continue;
                }

                i++;
            }
        }

        private int FindSlot(
            int hash,
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            int slot = hash & _mask;
            int startSlot = slot;
            do
            {
                if (!_occupied[slot])
                {
                    return -1;
                }

                if (_hashes[slot] == hash
                    && Matches(slot, in actionId, actionName, compositePartName, ownerId, ownerKey))
                {
                    return slot;
                }

                slot = (slot + 1) & _mask;
            }
            while (slot != startSlot);

            return -1;
        }

        private bool Matches(
            int slot,
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            return _actionIds[slot].Equals(actionId)
                   && _ownerIds[slot] == ownerId
                   && InputGlyphStringUtility.EqualsOrdinal(_actionNames[slot], Normalize(actionName))
                   && InputGlyphStringUtility.EqualsOrdinal(_compositePartNames[slot], Normalize(compositePartName))
                   && InputGlyphStringUtility.EqualsOrdinal(_ownerKeys[slot], Normalize(ownerKey));
        }

        private void SetInternal(
            int hash,
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            int slot = hash & _mask;
            while (_occupied[slot])
            {
                slot = (slot + 1) & _mask;
            }

            _actionIds[slot] = actionId;
            _actionNames[slot] = Normalize(actionName);
            _compositePartNames[slot] = Normalize(compositePartName);
            _ownerIds[slot] = ownerId;
            _ownerKeys[slot] = Normalize(ownerKey);
            _hashes[slot] = hash;
            _occupied[slot] = true;
            _count++;
        }

        private void RemoveAt(int slot)
        {
            ClearSlot(slot);
            _count--;

            int next = (slot + 1) & _mask;
            while (_occupied[next])
            {
                Guid actionId = _actionIds[next];
                string actionName = _actionNames[next];
                string compositePartName = _compositePartNames[next];
                int ownerId = _ownerIds[next];
                string ownerKey = _ownerKeys[next];
                int hash = _hashes[next];
                ClearSlot(next);
                _count--;
                SetInternal(hash, in actionId, actionName, compositePartName, ownerId, ownerKey);
                next = (next + 1) & _mask;
            }
        }

        private void ClearSlot(int slot)
        {
            _actionIds[slot] = Guid.Empty;
            _actionNames[slot] = null;
            _compositePartNames[slot] = null;
            _ownerIds[slot] = 0;
            _ownerKeys[slot] = null;
            _hashes[slot] = 0;
            _occupied[slot] = false;
        }

        private void Resize(int capacity)
        {
            Guid[] oldActionIds = _actionIds;
            string[] oldActionNames = _actionNames;
            string[] oldCompositePartNames = _compositePartNames;
            int[] oldOwnerIds = _ownerIds;
            string[] oldOwnerKeys = _ownerKeys;
            int[] oldHashes = _hashes;
            bool[] oldOccupied = _occupied;

            _actionIds = new Guid[capacity];
            _actionNames = new string[capacity];
            _compositePartNames = new string[capacity];
            _ownerIds = new int[capacity];
            _ownerKeys = new string[capacity];
            _hashes = new int[capacity];
            _occupied = new bool[capacity];
            _mask = capacity - 1;
            _count = 0;

            for (int i = 0; i < oldOccupied.Length; i++)
            {
                if (oldOccupied[i])
                {
                    Guid actionId = oldActionIds[i];
                    SetInternal(
                        oldHashes[i],
                        in actionId,
                        oldActionNames[i],
                        oldCompositePartNames[i],
                        oldOwnerIds[i],
                        oldOwnerKeys[i]);
                }
            }
        }
    }

    private sealed class CompositePartControlMap
    {
        private int[] _actionHashes;
        private Guid[] _actionIds;
        private string[] _partNames;
        private InputControl[] _controls;
        private int[] _hashes;
        private bool[] _occupied;
        private int _mask;
        private int _count;

        public CompositePartControlMap(int capacity)
        {
            int resolvedCapacity = NextPowerOfTwo(capacity);
            _actionHashes = new int[resolvedCapacity];
            _actionIds = new Guid[resolvedCapacity];
            _partNames = new string[resolvedCapacity];
            _controls = new InputControl[resolvedCapacity];
            _hashes = new int[resolvedCapacity];
            _occupied = new bool[resolvedCapacity];
            _mask = resolvedCapacity - 1;
        }

        public void Clear()
        {
            Array.Clear(_actionHashes, 0, _actionHashes.Length);
            Array.Clear(_actionIds, 0, _actionIds.Length);
            Array.Clear(_partNames, 0, _partNames.Length);
            Array.Clear(_controls, 0, _controls.Length);
            Array.Clear(_hashes, 0, _hashes.Length);
            Array.Clear(_occupied, 0, _occupied.Length);
            _count = 0;
        }

        public bool TryGet(InputAction action, string partName, out InputControl control)
        {
            control = null;
            int actionHash = RuntimeHelpers.GetHashCode(action);
            Guid actionId = action.id;
            string normalizedPartName = Normalize(partName);
            int hash = BuildCompositeHash(actionHash, in actionId, normalizedPartName);
            int slot = hash & _mask;
            int startSlot = slot;
            do
            {
                if (!_occupied[slot])
                {
                    return false;
                }

                if (_hashes[slot] == hash
                    && _actionHashes[slot] == actionHash
                    && _actionIds[slot].Equals(actionId)
                    && InputGlyphStringUtility.EqualsOrdinal(_partNames[slot], normalizedPartName))
                {
                    control = _controls[slot];
                    return true;
                }

                slot = (slot + 1) & _mask;
            }
            while (slot != startSlot);

            return false;
        }

        public void Set(InputAction action, string partName, InputControl control)
        {
            if ((_count + 1) * 2 >= _occupied.Length)
            {
                Resize(_occupied.Length << 1);
            }

            int actionHash = RuntimeHelpers.GetHashCode(action);
            Guid actionId = action.id;
            string normalizedPartName = Normalize(partName);
            int hash = BuildCompositeHash(actionHash, in actionId, normalizedPartName);
            SetInternal(hash, actionHash, in actionId, normalizedPartName, control);
        }

        private void SetInternal(int hash, int actionHash, in Guid actionId, string partName, InputControl control)
        {
            int slot = hash & _mask;
            while (_occupied[slot])
            {
                if (_hashes[slot] == hash
                    && _actionHashes[slot] == actionHash
                    && _actionIds[slot].Equals(actionId)
                    && InputGlyphStringUtility.EqualsOrdinal(_partNames[slot], partName))
                {
                    _controls[slot] = control;
                    return;
                }

                slot = (slot + 1) & _mask;
            }

            _hashes[slot] = hash;
            _actionHashes[slot] = actionHash;
            _actionIds[slot] = actionId;
            _partNames[slot] = partName;
            _controls[slot] = control;
            _occupied[slot] = true;
            _count++;
        }

        private void Resize(int capacity)
        {
            int[] oldActionHashes = _actionHashes;
            Guid[] oldActionIds = _actionIds;
            string[] oldPartNames = _partNames;
            InputControl[] oldControls = _controls;
            int[] oldHashes = _hashes;
            bool[] oldOccupied = _occupied;

            _actionHashes = new int[capacity];
            _actionIds = new Guid[capacity];
            _partNames = new string[capacity];
            _controls = new InputControl[capacity];
            _hashes = new int[capacity];
            _occupied = new bool[capacity];
            _mask = capacity - 1;
            _count = 0;

            for (int i = 0; i < oldOccupied.Length; i++)
            {
                if (oldOccupied[i])
                {
                    Guid actionId = oldActionIds[i];
                    SetInternal(oldHashes[i], oldActionHashes[i], in actionId, oldPartNames[i], oldControls[i]);
                }
            }
        }
    }

    private static int BuildHash(
        in Guid actionId,
        string actionName,
        string compositePartName,
        int ownerId,
        string ownerKey)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + actionId.GetHashCode();
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(Normalize(actionName));
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(Normalize(compositePartName));
            hash = (hash * 31) + ownerId;
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(Normalize(ownerKey));
            return hash == 0 ? 1 : hash;
        }
    }

    private static int BuildCompositeHash(int actionHash, in Guid actionId, string partName)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + actionHash;
            hash = (hash * 31) + actionId.GetHashCode();
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(partName);
            return hash == 0 ? 1 : hash;
        }
    }

    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
#endif
