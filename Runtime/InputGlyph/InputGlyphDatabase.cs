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

    [NonSerialized] private Dictionary<LookupKey, Entry> _entries;
    [NonSerialized] private Dictionary<string, Profile> _profilesById;
    [NonSerialized] private bool _cacheBuilt;

    /// <summary>
    /// 图标缺失时使用的占位图。
    /// </summary>
    public Sprite Placeholder => placeholderSprite;

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

    /// <summary>
    /// 查询指定 Profile 和图标键的明确条目。找不到时返回 false，不回退占位图。
    /// </summary>
    public bool TryGetGlyph(string profileId, string glyphKey, out Sprite sprite, out string tmpName)
    {
        sprite = null;
        tmpName = null;

        if (string.IsNullOrWhiteSpace(glyphKey) || !TryGetEntry(profileId, glyphKey, out Entry entry))
        {
            return false;
        }

        tmpName = ResolveTMPName(entry);
        sprite = entry.Sprite;
        return sprite != null || !string.IsNullOrEmpty(tmpName);
    }

    /// <summary>
    /// 查询指定 Profile 和图标键的 Sprite。找不到明确条目时返回 false。
    /// </summary>
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

    /// <summary>
    /// 查询指定 Profile 的绑定分组提示。
    /// </summary>
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

    private bool TryGetEntry(string profileId, string glyphKey, out Entry entry)
    {
        EnsureCache();
        entry = null;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        return _entries.TryGetValue(new LookupKey(profileId, glyphKey), out entry);
    }

    /// <summary>
    /// 构建设置时把 Profile 回退链展平为单一查找表，查询只做一次字典命中。
    /// </summary>
    private void BuildCache()
    {
        if (_entries == null)
        {
            _entries = new Dictionary<LookupKey, Entry>(64);
        }
        else
        {
            _entries.Clear();
        }

        if (_profilesById == null)
        {
            _profilesById = new Dictionary<string, Profile>(InitialProfileCapacity, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            _profilesById.Clear();
        }

        int profileCount = profiles != null ? profiles.Length : 0;
        for (int i = 0; i < profileCount; i++)
        {
            Profile profile = profiles[i];
            if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
            {
                continue;
            }

            profile.BuildCache();
            _profilesById[profile.Id] = profile;
        }

        if (profileCount == 0)
        {
            _cacheBuilt = true;
            return;
        }

        var glyphKeys = new List<string>(64);
        var seenGlyphKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < profileCount; i++)
        {
            Profile profile = profiles[i];
            if (profile == null)
            {
                continue;
            }

            profile.AppendGlyphKeys(glyphKeys, seenGlyphKeys);
        }

        var queue = new List<Profile>(InitialProfileCapacity);
        var visited = new List<string>(InitialProfileCapacity);
        for (int i = 0; i < profileCount; i++)
        {
            Profile owner = profiles[i];
            if (owner == null || string.IsNullOrWhiteSpace(owner.Id))
            {
                continue;
            }

            for (int keyIndex = 0; keyIndex < glyphKeys.Count; keyIndex++)
            {
                string glyphKey = glyphKeys[keyIndex];
                Entry entry = ResolveEntry(owner, glyphKey, queue, visited);
                if (entry != null)
                {
                    _entries[new LookupKey(owner.Id, glyphKey)] = entry;
                }
            }
        }

        _cacheBuilt = true;
    }

    private Entry ResolveEntry(Profile owner, string glyphKey, List<Profile> queue, List<string> visited)
    {
        queue.Clear();
        visited.Clear();
        queue.Add(owner);
        visited.Add(owner.Id);

        for (int index = 0; index < queue.Count; index++)
        {
            Profile current = queue[index];
            if (current.TryGetEntry(glyphKey, out Entry entry))
            {
                return entry;
            }

            string[] fallbackIds = current.FallbackProfileIds;
            if (fallbackIds == null)
            {
                continue;
            }

            for (int i = 0; i < fallbackIds.Length; i++)
            {
                string fallbackId = fallbackIds[i];
                if (string.IsNullOrWhiteSpace(fallbackId) || ContainsId(visited, fallbackId))
                {
                    continue;
                }

                if (_profilesById.TryGetValue(fallbackId, out Profile fallbackProfile))
                {
                    visited.Add(fallbackId);
                    queue.Add(fallbackProfile);
                }
            }
        }

        return null;
    }

    private static bool ContainsId(List<string> ids, string id)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        if (!string.IsNullOrEmpty(entry.TMPSpriteName))
        {
            return entry.TMPSpriteName;
        }

        Sprite sprite = entry.Sprite;
        return sprite != null ? sprite.name : null;
    }

    [Serializable]
    private sealed class Profile
    {
        [SerializeField] private string id;
        [SerializeField] private string[] fallbackProfileIds = Array.Empty<string>();
        [SerializeField] private string[] bindingGroupHints = Array.Empty<string>();
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        [NonSerialized] private Dictionary<string, Entry> _entriesByGlyphKey;

        public string Id => id;
        public string[] FallbackProfileIds => fallbackProfileIds;
        public string[] BindingGroupHints => bindingGroupHints;

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

        public void AppendGlyphKeys(List<string> glyphKeys, HashSet<string> seenGlyphKeys)
        {
            if (_entriesByGlyphKey == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Entry> pair in _entriesByGlyphKey)
            {
                if (seenGlyphKeys.Add(pair.Key))
                {
                    glyphKeys.Add(pair.Key);
                }
            }
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

        public string[] ControlPaths => controlPaths;
        public Sprite Sprite => sprite;
        public string TMPSpriteName => tmpSpriteName;
    }

    private readonly struct LookupKey : IEquatable<LookupKey>
    {
        private readonly string _profileId;
        private readonly string _glyphKey;
        private readonly int _hash;

        internal LookupKey(string profileId, string glyphKey)
        {
            _profileId = profileId;
            _glyphKey = glyphKey;
            unchecked
            {
                _hash = (StringComparer.OrdinalIgnoreCase.GetHashCode(profileId) * 397)
                        ^ StringComparer.OrdinalIgnoreCase.GetHashCode(glyphKey);
            }
        }

        public bool Equals(LookupKey other)
        {
            return string.Equals(_profileId, other._profileId, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(_glyphKey, other._glyphKey, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is LookupKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hash;
        }
    }
}
#endif
