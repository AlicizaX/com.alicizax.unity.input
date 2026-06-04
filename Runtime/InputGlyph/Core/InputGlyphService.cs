#if INPUTSYSTEM_SUPPORT
using System;
using System.Runtime.CompilerServices;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public static class InputGlyphService
{
    private const int InitialCacheCapacity = 64;
    private const int InitialBuilderCapacity = 64;
    private static readonly InputGlyphStringMap<string> DisplayNameCache = new InputGlyphStringMap<string>(InitialCacheCapacity);
    private static readonly InputGlyphStringMap<string> GlyphKeyCache = new InputGlyphStringMap<string>(InitialCacheCapacity);
    private static readonly InputGlyphStringMap<string> SpriteTagCache = new InputGlyphStringMap<string>(InitialCacheCapacity);
    private static readonly BindingSelectionMap BindingSelections = new BindingSelectionMap(InitialCacheCapacity);

    private static InputGlyphDatabase _database;

    public static void SetDatabase(InputGlyphDatabase database)
    {
        _database = database;
        BindingSelections.Clear();
    }

    public static void ClearBindingCache()
    {
        BindingSelections.Clear();
    }

    public static string ResolveProfileId(
        int vendorId,
        int productId,
        string controlScheme,
        string deviceName,
        string layout,
        string interfaceName,
        string manufacturer,
        string product)
    {
        InputGlyphDatabase db = _database;
        if (db == null)
        {
            return IsKeyboardMouseLike(controlScheme, layout, deviceName)
                ? InputGlyphProfileIds.KeyboardMouse
                : InputGlyphProfileIds.GenericGamepad;
        }

        return db.ResolveProfileId(
            vendorId,
            productId,
            controlScheme,
            deviceName,
            layout,
            interfaceName,
            manufacturer,
            product);
    }

    public static string GetBindingControlPath(
        InputAction action,
        string compositePartName = null)
    {
        return GetBindingControlPath(action, compositePartName, InputDeviceWatcher.CurrentContext);
    }

    public static string GetBindingControlPath(
        InputAction action,
        string compositePartName,
        InputGlyphContext context)
    {
        context = ResolveContext(context);
        return TryGetBindingControl(action, compositePartName, context, out InputBinding binding)
            ? GetEffectivePath(binding)
            : string.Empty;
    }

    public static string GetBindingControlPath(
        InputActionReference actionReference,
        string compositePartName = null)
    {
        return GetBindingControlPath(actionReference != null ? actionReference.action : null, compositePartName);
    }

    public static string GetBindingControlPath(
        InputActionReference actionReference,
        string compositePartName,
        InputGlyphContext context)
    {
        return GetBindingControlPath(actionReference != null ? actionReference.action : null, compositePartName, context);
    }

    public static bool TryGetTMPTagForActionPath(
        InputAction action,
        string compositePartName,
        out string tag,
        out string displayFallback,
        InputGlyphDatabase db = null)
    {
        return TryGetTMPTagForActionPath(action, compositePartName, InputDeviceWatcher.CurrentContext, out tag, out displayFallback, db);
    }

    public static bool TryGetTMPTagForActionPath(
        InputAction action,
        string compositePartName,
        InputGlyphContext context,
        out string tag,
        out string displayFallback,
        InputGlyphDatabase db = null)
    {
        context = ResolveContext(context);
        string controlPath = GetBindingControlPath(action, compositePartName, context);
        return TryGetTMPTagForControlPath(controlPath, context.ProfileId, out tag, out displayFallback, db);
    }

    public static bool TryGetTMPTagForActionPath(
        InputActionReference actionReference,
        string compositePartName,
        InputGlyphContext context,
        out string tag,
        out string displayFallback,
        InputGlyphDatabase db = null)
    {
        return TryGetTMPTagForActionPath(
            actionReference != null ? actionReference.action : null,
            compositePartName,
            context,
            out tag,
            out displayFallback,
            db);
    }

    public static bool TryGetUISpriteForActionPath(
        InputAction action,
        string compositePartName,
        out Sprite sprite,
        InputGlyphDatabase db = null)
    {
        return TryGetUISpriteForActionPath(action, compositePartName, InputDeviceWatcher.CurrentContext, out sprite, db);
    }

    public static bool TryGetUISpriteForActionPath(
        InputAction action,
        string compositePartName,
        InputGlyphContext context,
        out Sprite sprite,
        InputGlyphDatabase db = null)
    {
        context = ResolveContext(context);
        string controlPath = GetBindingControlPath(action, compositePartName, context);
        return TryGetUISpriteForControlPath(controlPath, context.ProfileId, out sprite, db);
    }

    public static bool TryGetUISpriteForActionPath(
        InputActionReference actionReference,
        string compositePartName,
        InputGlyphContext context,
        out Sprite sprite,
        InputGlyphDatabase db = null)
    {
        return TryGetUISpriteForActionPath(
            actionReference != null ? actionReference.action : null,
            compositePartName,
            context,
            out sprite,
            db);
    }

    public static bool TryGetTMPTagForControlPath(
        string controlPath,
        string profileId,
        out string tag,
        out string displayFallback,
        InputGlyphDatabase db = null)
    {
        displayFallback = GetDisplayNameFromControlPath(controlPath);
        tag = null;
        string glyphKey = GetGlyphKeyFromControlPath(controlPath);
        if (string.IsNullOrEmpty(glyphKey))
        {
            return false;
        }

        if (db == null)
        {
            db = _database;
        }
        if (db == null)
        {
            return false;
        }

        if (db.TryGetTMPName(glyphKey, profileId, out string spriteName, out Sprite sprite))
        {
            tag = GetSpriteTag(sprite, spriteName);
            return tag != null;
        }

        if (db.TryGetSprite(glyphKey, profileId, out sprite))
        {
            tag = GetSpriteTag(sprite, null);
            return tag != null;
        }

        return false;
    }

    public static bool TryGetUISpriteForControlPath(
        string controlPath,
        string profileId,
        out Sprite sprite,
        InputGlyphDatabase db = null)
    {
        sprite = null;
        string glyphKey = GetGlyphKeyFromControlPath(controlPath);
        if (string.IsNullOrEmpty(glyphKey))
        {
            return false;
        }

        if (db == null)
        {
            db = _database;
        }
        return db != null && db.TryGetSprite(glyphKey, profileId, out sprite);
    }

    public static string GetDisplayNameFromInputAction(
        InputAction action,
        string compositePartName = null)
    {
        return GetDisplayNameFromInputAction(action, compositePartName, InputDeviceWatcher.CurrentContext);
    }

    public static string GetDisplayNameFromInputAction(
        InputAction action,
        string compositePartName,
        InputGlyphContext context)
    {
        context = ResolveContext(context);
        if (!TryGetBindingControl(action, compositePartName, context, out InputBinding binding))
        {
            return string.Empty;
        }

        string display = binding.ToDisplayString();
        return string.IsNullOrEmpty(display) ? GetDisplayNameFromControlPath(GetEffectivePath(binding)) : display;
    }

    public static string GetDisplayNameFromControlPath(string controlPath)
    {
        if (string.IsNullOrWhiteSpace(controlPath))
        {
            return string.Empty;
        }

        if (DisplayNameCache.TryGetValue(controlPath, out string cachedDisplayName))
        {
            return cachedDisplayName;
        }

        string humanReadable = InputControlPath.ToHumanReadableString(controlPath, InputControlPath.HumanReadableStringOptions.OmitDevice);
        if (!string.IsNullOrWhiteSpace(humanReadable))
        {
            DisplayNameCache.Set(controlPath, humanReadable);
            return humanReadable;
        }

        string fallback = BuildDisplayFallback(controlPath);
        DisplayNameCache.Set(controlPath, fallback);
        return fallback;
    }

    public static bool TryGetBindingControl(
        InputAction action,
        string compositePartName,
        InputGlyphContext context,
        out InputBinding binding)
    {
        binding = default;
        context = ResolveContext(context);
        if (action == null || context == null)
        {
            return false;
        }

        string profileId = context.ProfileId;
        if (BindingSelections.TryGet(action, compositePartName, profileId, out int cachedBindingIndex))
        {
            if (cachedBindingIndex < 0)
            {
                return false;
            }

            if (cachedBindingIndex < action.bindings.Count)
            {
                binding = action.bindings[cachedBindingIndex];
                return true;
            }

            BindingSelections.Remove(action, compositePartName, profileId);
        }

        InputDeviceProfileConfig profile = null;
        InputGlyphDatabase db = _database;
        if (db != null)
        {
            db.TryGetProfile(profileId, out profile);
        }

        int bindingIndex = FindBestBindingIndex(action, compositePartName, context, profile);
        BindingSelections.Set(action, compositePartName, profileId, bindingIndex);
        if (bindingIndex < 0)
        {
            return false;
        }

        binding = action.bindings[bindingIndex];
        return true;
    }

    private static int FindBestBindingIndex(
        InputAction action,
        string compositePartName,
        InputGlyphContext context,
        InputDeviceProfileConfig profile)
    {
        int bestScore = int.MinValue;
        int bestBindingIndex = -1;
        bool requireCompositePart = !string.IsNullOrEmpty(compositePartName);
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding candidate = action.bindings[i];
            if (candidate.isComposite)
            {
                continue;
            }

            if (requireCompositePart)
            {
                if (!candidate.isPartOfComposite
                    || !InputGlyphStringUtility.EqualsIgnoreCase(candidate.name, compositePartName))
                {
                    continue;
                }
            }
            else if (candidate.isPartOfComposite)
            {
                continue;
            }

            string path = GetEffectivePath(candidate);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            int score = ScoreBinding(candidate, path, context, profile);
            if (score > bestScore)
            {
                bestScore = score;
                bestBindingIndex = i;
            }
        }

        return bestScore > int.MinValue ? bestBindingIndex : -1;
    }

    public static string GetGlyphKeyFromControlPath(string controlPath)
    {
        if (string.IsNullOrWhiteSpace(controlPath))
        {
            return string.Empty;
        }

        if (GlyphKeyCache.TryGetValue(controlPath, out string cachedGlyphKey))
        {
            return cachedGlyphKey;
        }

        using (Utf16ValueStringBuilder sb = ZString.CreateStringBuilder())
        {
            int layoutStart = controlPath.IndexOf('<');
            int layoutEnd = controlPath.IndexOf('>');
            int pathStart = controlPath.IndexOf('/');
            if (layoutStart < 0 || layoutEnd <= layoutStart || pathStart < 0 || pathStart >= controlPath.Length - 1)
            {
                GlyphKeyCache.Set(controlPath, string.Empty);
                return string.Empty;
            }

            int layoutStartIndex = layoutStart + 1;
            int layoutLength = layoutEnd - layoutStart - 1;
            if (ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "keyboard"))
            {
                sb.Append("keyboard");
            }
            else if (ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "mouse"))
            {
                sb.Append("mouse");
            }
            else if (ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "gamepad")
                     || ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "controller")
                     || ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "xinput")
                     || ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "dualshock")
                     || ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "dualsense")
                     || ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "switch")
                     || ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "nintendo")
                     || ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "joy-con")
                     || ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "joycon"))
            {
                sb.Append("gamepad");
            }
            else if (ContainsIgnoreCase(controlPath, layoutStartIndex, layoutLength, "joystick"))
            {
                sb.Append("joystick");
            }
            else
            {
                for (int i = 0; i < layoutLength; i++)
                {
                    sb.Append(InputGlyphStringUtility.ToLowerInvariantFast(controlPath[layoutStartIndex + i]));
                }
            }

            sb.Append('/');
            for (int i = pathStart + 1; i < controlPath.Length; i++)
            {
                char value = controlPath[i];
                if (value == '\\')
                {
                    value = '/';
                }

                sb.Append(InputGlyphStringUtility.ToLowerInvariantFast(value));
            }

            string glyphKey = sb.ToString();
            GlyphKeyCache.Set(controlPath, glyphKey);
            return glyphKey;
        }
    }

    private static int ScoreBinding(
        InputBinding binding,
        string path,
        InputGlyphContext context,
        InputDeviceProfileConfig profile)
    {
        int score = 0;
        if (MatchesBindingGroups(binding.groups, profile))
        {
            score += 100;
        }
        else if (!string.IsNullOrWhiteSpace(binding.groups))
        {
            score -= 20;
        }

        if (MatchesControlPath(path, context))
        {
            score += 60;
        }

        if (!binding.isPartOfComposite)
        {
            score += 5;
        }

        return score;
    }

    private static bool MatchesBindingGroups(string groups, InputDeviceProfileConfig profile)
    {
        if (string.IsNullOrWhiteSpace(groups) || profile == null || profile.bindingGroupHints == null)
        {
            return false;
        }

        int tokenStart = 0;
        for (int i = 0; i <= groups.Length; i++)
        {
            if (i < groups.Length && groups[i] != InputBinding.Separator)
            {
                continue;
            }

            int tokenLength = i - tokenStart;
            while (tokenLength > 0 && char.IsWhiteSpace(groups[tokenStart]))
            {
                tokenStart++;
                tokenLength--;
            }

            while (tokenLength > 0 && char.IsWhiteSpace(groups[tokenStart + tokenLength - 1]))
            {
                tokenLength--;
            }

            if (tokenLength > 0 && ContainsAny(groups, tokenStart, tokenLength, profile.bindingGroupHints))
            {
                return true;
            }

            tokenStart = i + 1;
        }

        return false;
    }

    private static string BuildDisplayFallback(string controlPath)
    {
        int start = FindLastControlPathSegmentStart(controlPath);
        int end = controlPath.Length - 1;
        while (start <= end && IsDisplayTrimChar(controlPath[start]))
        {
            start++;
        }

        while (end >= start && IsDisplayTrimChar(controlPath[end]))
        {
            end--;
        }

        if (start > end)
        {
            return string.Empty;
        }

        using (Utf16ValueStringBuilder sb = ZString.CreateStringBuilder())
        {
            for (int i = start; i <= end; i++)
            {
                sb.Append(controlPath[i]);
            }

            return sb.ToString();
        }
    }

    private static int FindLastControlPathSegmentStart(string controlPath)
    {
        for (int i = controlPath.Length - 1; i >= 0; i--)
        {
            if (controlPath[i] == '/')
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static bool IsDisplayTrimChar(char value)
    {
        return value == '{'
               || value == '}'
               || value == '<'
               || value == '>'
               || value == '\''
               || value == '"';
    }

    private static bool MatchesControlPath(string path, InputGlyphContext context)
    {
        if (string.IsNullOrWhiteSpace(path) || context == null)
        {
            return false;
        }

        if (InputGlyphProfileIds.IsKeyboardMouse(context.ProfileId))
        {
            return InputGlyphStringUtility.StartsWithIgnoreCase(path, "<Keyboard>")
                   || InputGlyphStringUtility.StartsWithIgnoreCase(path, "<Mouse>");
        }

        return InputGlyphStringUtility.StartsWithIgnoreCase(path, "<Gamepad>")
               || InputGlyphStringUtility.StartsWithIgnoreCase(path, "<Joystick>")
               || InputGlyphStringUtility.ContainsIgnoreCase(path, context.ProfileId);
    }

    private static bool ContainsAny(string source, int startIndex, int length, string[] hints)
    {
        if (string.IsNullOrEmpty(source) || hints == null || length <= 0)
        {
            return false;
        }

        for (int i = 0; i < hints.Length; i++)
        {
            if (ContainsIgnoreCase(source, startIndex, length, hints[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIgnoreCase(string source, int startIndex, int length, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value) || value.Length > length)
        {
            return false;
        }

        int end = startIndex + length - value.Length;
        for (int i = startIndex; i <= end; i++)
        {
            int valueIndex = 0;
            while (valueIndex < value.Length
                   && InputGlyphStringUtility.ToUpperInvariantFast(source[i + valueIndex])
                   == InputGlyphStringUtility.ToUpperInvariantFast(value[valueIndex]))
            {
                valueIndex++;
            }

            if (valueIndex == value.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetSpriteTag(Sprite sprite, string spriteName)
    {
        string resolvedSpriteName = !string.IsNullOrEmpty(spriteName) ? spriteName : (sprite != null ? sprite.name : null);
        if (string.IsNullOrEmpty(resolvedSpriteName))
        {
            return null;
        }

        if (SpriteTagCache.TryGetValue(resolvedSpriteName, out string cachedSpriteTag))
        {
            return cachedSpriteTag;
        }

        string cachedTag = ZString.Concat("<sprite name=\"", resolvedSpriteName, "\">");
        SpriteTagCache.Set(resolvedSpriteName, cachedTag);
        return cachedTag;
    }

    private static bool IsKeyboardMouseLike(string controlScheme, string layout, string deviceName)
    {
        return InputGlyphStringUtility.ContainsIgnoreCase(controlScheme, "keyboard")
               || InputGlyphStringUtility.ContainsIgnoreCase(controlScheme, "mouse")
               || InputGlyphStringUtility.ContainsIgnoreCase(layout, "keyboard")
               || InputGlyphStringUtility.ContainsIgnoreCase(layout, "mouse")
               || InputGlyphStringUtility.ContainsIgnoreCase(deviceName, "keyboard")
               || InputGlyphStringUtility.ContainsIgnoreCase(deviceName, "mouse");
    }

    private static InputGlyphContext ResolveContext(InputGlyphContext context)
    {
        return context != null ? context : InputDeviceWatcher.CurrentContext;
    }

    private static string GetEffectivePath(InputBinding binding)
    {
        return string.IsNullOrWhiteSpace(binding.effectivePath) ? binding.path : binding.effectivePath;
    }

    private sealed class BindingSelectionMap
    {
        private int[] _actionHashes;
        private Guid[] _actionIds;
        private string[] _profileIds;
        private string[] _compositePartNames;
        private int[] _hashes;
        private int[] _bindingIndices;
        private bool[] _occupied;
        private int _mask;
        private int _count;

        public BindingSelectionMap(int capacity)
        {
            int resolvedCapacity = NextPowerOfTwo(capacity);
            _actionHashes = new int[resolvedCapacity];
            _actionIds = new Guid[resolvedCapacity];
            _profileIds = new string[resolvedCapacity];
            _compositePartNames = new string[resolvedCapacity];
            _hashes = new int[resolvedCapacity];
            _bindingIndices = new int[resolvedCapacity];
            _occupied = new bool[resolvedCapacity];
            _mask = resolvedCapacity - 1;
        }

        public void Clear()
        {
            Array.Clear(_actionHashes, 0, _actionHashes.Length);
            Array.Clear(_actionIds, 0, _actionIds.Length);
            Array.Clear(_profileIds, 0, _profileIds.Length);
            Array.Clear(_compositePartNames, 0, _compositePartNames.Length);
            Array.Clear(_hashes, 0, _hashes.Length);
            Array.Clear(_bindingIndices, 0, _bindingIndices.Length);
            Array.Clear(_occupied, 0, _occupied.Length);
            _count = 0;
        }

        public bool TryGet(InputAction action, string compositePartName, string profileId, out int bindingIndex)
        {
            bindingIndex = -1;
            int actionHash = RuntimeHelpers.GetHashCode(action);
            Guid actionId = action.id;
            string normalizedCompositePartName = Normalize(compositePartName);
            string normalizedProfileId = Normalize(profileId);
            int hash = BuildSelectionHash(actionHash, in actionId, normalizedCompositePartName, normalizedProfileId);
            int slot = FindSlot(hash, actionHash, in actionId, normalizedCompositePartName, normalizedProfileId);
            if (slot < 0)
            {
                return false;
            }

            bindingIndex = _bindingIndices[slot];
            return true;
        }

        public void Set(InputAction action, string compositePartName, string profileId, int bindingIndex)
        {
            if ((_count + 1) * 2 >= _occupied.Length)
            {
                Resize(_occupied.Length << 1);
            }

            int actionHash = RuntimeHelpers.GetHashCode(action);
            Guid actionId = action.id;
            string normalizedCompositePartName = Normalize(compositePartName);
            string normalizedProfileId = Normalize(profileId);
            int hash = BuildSelectionHash(actionHash, in actionId, normalizedCompositePartName, normalizedProfileId);
            SetInternal(hash, actionHash, in actionId, normalizedCompositePartName, normalizedProfileId, bindingIndex);
        }

        public bool Remove(InputAction action, string compositePartName, string profileId)
        {
            int actionHash = RuntimeHelpers.GetHashCode(action);
            Guid actionId = action.id;
            string normalizedCompositePartName = Normalize(compositePartName);
            string normalizedProfileId = Normalize(profileId);
            int hash = BuildSelectionHash(actionHash, in actionId, normalizedCompositePartName, normalizedProfileId);
            int slot = FindSlot(hash, actionHash, in actionId, normalizedCompositePartName, normalizedProfileId);
            if (slot < 0)
            {
                return false;
            }

            RemoveAt(slot);
            return true;
        }

        private int FindSlot(
            int hash,
            int actionHash,
            in Guid actionId,
            string compositePartName,
            string profileId)
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
                    && _actionHashes[slot] == actionHash
                    && _actionIds[slot].Equals(actionId)
                    && InputGlyphStringUtility.EqualsOrdinal(_compositePartNames[slot], compositePartName)
                    && InputGlyphStringUtility.EqualsOrdinal(_profileIds[slot], profileId))
                {
                    return slot;
                }

                slot = (slot + 1) & _mask;
            }
            while (slot != startSlot);

            return -1;
        }

        private void SetInternal(
            int hash,
            int actionHash,
            in Guid actionId,
            string compositePartName,
            string profileId,
            int bindingIndex)
        {
            int slot = hash & _mask;
            while (_occupied[slot])
            {
                if (_hashes[slot] == hash
                    && _actionHashes[slot] == actionHash
                    && _actionIds[slot].Equals(actionId)
                    && InputGlyphStringUtility.EqualsOrdinal(_compositePartNames[slot], compositePartName)
                    && InputGlyphStringUtility.EqualsOrdinal(_profileIds[slot], profileId))
                {
                    _bindingIndices[slot] = bindingIndex;
                    return;
                }

                slot = (slot + 1) & _mask;
            }

            _actionHashes[slot] = actionHash;
            _actionIds[slot] = actionId;
            _profileIds[slot] = profileId;
            _compositePartNames[slot] = compositePartName;
            _hashes[slot] = hash;
            _bindingIndices[slot] = bindingIndex;
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
                int actionHash = _actionHashes[next];
                Guid actionId = _actionIds[next];
                string profileId = _profileIds[next];
                string compositePartName = _compositePartNames[next];
                int hash = _hashes[next];
                int bindingIndex = _bindingIndices[next];
                ClearSlot(next);
                _count--;
                SetInternal(hash, actionHash, in actionId, compositePartName, profileId, bindingIndex);
                next = (next + 1) & _mask;
            }
        }

        private void ClearSlot(int slot)
        {
            _actionHashes[slot] = 0;
            _actionIds[slot] = Guid.Empty;
            _profileIds[slot] = null;
            _compositePartNames[slot] = null;
            _hashes[slot] = 0;
            _bindingIndices[slot] = 0;
            _occupied[slot] = false;
        }

        private void Resize(int capacity)
        {
            int[] oldActionHashes = _actionHashes;
            Guid[] oldActionIds = _actionIds;
            string[] oldProfileIds = _profileIds;
            string[] oldCompositePartNames = _compositePartNames;
            int[] oldHashes = _hashes;
            int[] oldBindingIndices = _bindingIndices;
            bool[] oldOccupied = _occupied;

            _actionHashes = new int[capacity];
            _actionIds = new Guid[capacity];
            _profileIds = new string[capacity];
            _compositePartNames = new string[capacity];
            _hashes = new int[capacity];
            _bindingIndices = new int[capacity];
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
                        oldActionHashes[i],
                        in actionId,
                        oldCompositePartNames[i],
                        oldProfileIds[i],
                        oldBindingIndices[i]);
                }
            }
        }
    }

    private static int BuildSelectionHash(
        int actionHash,
        in Guid actionId,
        string compositePartName,
        string profileId)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + actionHash;
            hash = (hash * 31) + actionId.GetHashCode();
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(compositePartName);
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(profileId);
            return hash == 0 ? 1 : hash;
        }
    }

    private static string Normalize(string value)
    {
        return value ?? string.Empty;
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
