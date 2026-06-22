#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "AlicizaX/Input/Glyph Database", fileName = "InputGlyphDatabase")]
public sealed class InputGlyphDatabase : ScriptableObject
{
    private const int InitialProfileCapacity = 8;

    [SerializeField] private Sprite placeholderSprite;
    [SerializeField] private Profile[] profiles = Array.Empty<Profile>();

    [NonSerialized] private Dictionary<string, Profile> _profilesById;
    [NonSerialized] private string[] _fallbackQueue = Array.Empty<string>();
    [NonSerialized] private string[] _visitedProfileIds = Array.Empty<string>();
    [NonSerialized] private bool _cacheBuilt;

    private void OnEnable()
    {
        BuildCache();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        BuildCache();
    }

    public void EditorRefreshCache()
    {
        BuildCache();
    }
#endif

    public bool TryGetGlyph(string profileId, string glyphKey, out Sprite sprite, out string tmpName)
    {
        EnsureCache();
        sprite = null;
        tmpName = null;

        if (string.IsNullOrWhiteSpace(glyphKey))
        {
            return TryGetPlaceholder(out sprite, out tmpName);
        }

        if (TryGetEntry(profileId, glyphKey, out Entry entry))
        {
            tmpName = ResolveTMPName(entry);
            if (entry.Sprite == null && string.IsNullOrEmpty(tmpName))
            {
                return TryGetPlaceholder(out sprite, out tmpName);
            }

            sprite = entry.Sprite;
            return true;
        }

        return TryGetPlaceholder(out sprite, out tmpName);
    }

    public bool TryGetSprite(string profileId, string glyphKey, out Sprite sprite)
    {
        sprite = null;
        if (!TryGetGlyph(profileId, glyphKey, out Sprite resolvedSprite, out _))
        {
            return false;
        }

        sprite = resolvedSprite;
        return sprite != null;
    }

    public bool TryGetBindingGroupHints(string profileId, out string[] bindingGroupHints)
    {
        EnsureCache();
        bindingGroupHints = null;
        if (!TryGetProfile(profileId, out Profile profile))
        {
            return false;
        }

        bindingGroupHints = profile.BindingGroupHints;
        return bindingGroupHints != null && bindingGroupHints.Length > 0;
    }

    private bool TryGetProfile(string profileId, out Profile profile)
    {
        EnsureCache();
        profile = null;
        return !string.IsNullOrWhiteSpace(profileId)
               && _profilesById != null
               && _profilesById.TryGetValue(profileId, out profile)
               && profile != null;
    }

    private bool TryGetPlaceholder(out Sprite sprite, out string tmpName)
    {
        sprite = placeholderSprite;
        tmpName = placeholderSprite != null ? placeholderSprite.name : null;
        return placeholderSprite != null;
    }

    private bool TryGetEntry(string profileId, string glyphKey, out Entry entry)
    {
        entry = null;

        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        int profileCount = profiles != null ? profiles.Length : 0;
        int queueCount = 0;
        int visitedCount = 0;
        EnsureFallbackScratchCapacity(Mathf.Max(InitialProfileCapacity, profileCount + 1));
        EnqueueProfile(profileId, ref queueCount);

        int readIndex = 0;
        while (readIndex < queueCount)
        {
            string currentProfileId = _fallbackQueue[readIndex];
            readIndex++;

            if (HasVisited(currentProfileId, visitedCount))
            {
                continue;
            }

            AddVisited(currentProfileId, ref visitedCount);

            if (!TryGetProfile(currentProfileId, out Profile profile))
            {
                continue;
            }

            if (profile.TryGetEntry(glyphKey, out entry))
            {
                ClearFallbackScratch(queueCount, visitedCount);
                return true;
            }

            string[] fallbackProfileIds = profile.FallbackProfileIds;
            if (fallbackProfileIds == null)
            {
                continue;
            }

            for (int i = 0; i < fallbackProfileIds.Length; i++)
            {
                string fallbackProfileId = fallbackProfileIds[i];
                if (!string.IsNullOrWhiteSpace(fallbackProfileId) && !HasVisited(fallbackProfileId, visitedCount))
                {
                    EnqueueProfile(fallbackProfileId, ref queueCount);
                }
            }
        }

        ClearFallbackScratch(queueCount, visitedCount);
        return false;
    }

    private void BuildCache()
    {
        if (_profilesById == null)
        {
            _profilesById = new Dictionary<string, Profile>(
                InitialProfileCapacity,
                StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            _profilesById.Clear();
        }

        int count = profiles != null ? profiles.Length : 0;
        for (int i = 0; i < count; i++)
        {
            Profile profile = profiles[i];
            if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
            {
                continue;
            }

            profile.BuildCache();
            _profilesById[profile.Id] = profile;
        }

        _cacheBuilt = true;
    }

    private void EnsureCache()
    {
        if (!_cacheBuilt)
        {
            BuildCache();
        }
    }

    private static string ResolveTMPName(Entry entry)
    {
        if (entry == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(entry.TMPSpriteName))
        {
            return entry.TMPSpriteName;
        }

        Sprite sprite = entry.Sprite;
        return sprite != null ? sprite.name : null;
    }

    private void EnsureFallbackScratchCapacity(int capacity)
    {
        if (_fallbackQueue.Length < capacity)
        {
            Array.Resize(ref _fallbackQueue, capacity);
        }

        if (_visitedProfileIds.Length < capacity)
        {
            Array.Resize(ref _visitedProfileIds, capacity);
        }
    }

    private void EnqueueProfile(string profileId, ref int queueCount)
    {
        if (queueCount == _fallbackQueue.Length)
        {
            Array.Resize(ref _fallbackQueue, _fallbackQueue.Length == 0 ? InitialProfileCapacity : _fallbackQueue.Length << 1);
        }

        _fallbackQueue[queueCount] = profileId;
        queueCount++;
    }

    private void AddVisited(string profileId, ref int visitedCount)
    {
        if (visitedCount == _visitedProfileIds.Length)
        {
            Array.Resize(ref _visitedProfileIds, _visitedProfileIds.Length == 0 ? InitialProfileCapacity : _visitedProfileIds.Length << 1);
        }

        _visitedProfileIds[visitedCount] = profileId;
        visitedCount++;
    }

    private bool HasVisited(string profileId, int visitedCount)
    {
        for (int i = 0; i < visitedCount; i++)
        {
            if (string.Equals(_visitedProfileIds[i], profileId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ClearFallbackScratch(int queueCount, int visitedCount)
    {
        Array.Clear(_fallbackQueue, 0, queueCount);
        Array.Clear(_visitedProfileIds, 0, visitedCount);
    }

    [Serializable]
    private sealed class Profile
    {
        [SerializeField] private string id;
        [SerializeField] private string[] fallbackProfileIds = Array.Empty<string>();
        [SerializeField] private string[] bindingGroupHints = Array.Empty<string>();
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [NonSerialized] private Dictionary<string, Entry> _entriesByGlyphKey;

        public string Id
        {
            get
            {
                return id;
            }
        }

        public string[] FallbackProfileIds
        {
            get
            {
                return fallbackProfileIds;
            }
        }

        public string[] BindingGroupHints
        {
            get
            {
                return bindingGroupHints;
            }
        }

        public void BuildCache()
        {
            if (_entriesByGlyphKey == null)
            {
                _entriesByGlyphKey = new Dictionary<string, Entry>(
                    entries != null ? Mathf.Max(8, entries.Length * 2) : 8,
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _entriesByGlyphKey.Clear();
            }

            int count = entries != null ? entries.Length : 0;
            for (int i = 0; i < count; i++)
            {
                Entry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                RegisterEntry(entry);
            }
        }

        public bool TryGetEntry(string glyphKey, out Entry entry)
        {
            entry = null;
            return !string.IsNullOrWhiteSpace(glyphKey)
                   && _entriesByGlyphKey != null
                   && _entriesByGlyphKey.TryGetValue(glyphKey, out entry)
                   && entry != null;
        }

        private void RegisterEntry(Entry entry)
        {
            string[] controlPaths = entry.ControlPaths;
            int count = controlPaths != null ? controlPaths.Length : 0;
            for (int i = 0; i < count; i++)
            {
                string glyphKey = InputGlyphPathUtility.GetGlyphKeyFromControlPath(controlPaths[i]);
                if (string.IsNullOrWhiteSpace(glyphKey))
                {
                    glyphKey = controlPaths[i];
                }

                if (!string.IsNullOrWhiteSpace(glyphKey))
                {
                    _entriesByGlyphKey[glyphKey] = entry;
                }
            }
        }
    }

    [Serializable]
    private sealed class Entry
    {
        [SerializeField] private string[] controlPaths = Array.Empty<string>();
        [SerializeField] private Sprite sprite;
        [SerializeField] private string tmpSpriteName;

        public string[] ControlPaths
        {
            get
            {
                return controlPaths;
            }
        }

        public Sprite Sprite
        {
            get
            {
                return sprite;
            }
        }

        public string TMPSpriteName
        {
            get
            {
                return tmpSpriteName;
            }
        }
    }
}
#endif
