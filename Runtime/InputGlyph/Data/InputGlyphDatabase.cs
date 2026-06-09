#if INPUTSYSTEM_SUPPORT
using System;
using Cysharp.Text;
using UnityEngine;

/// <summary>
/// 描述用于匹配输入设备的条件和优先级。
/// </summary>
[Serializable]
public sealed class InputDeviceMatcher
{
    /// <summary>
    /// 匹配成功时附加的优先级分数。
    /// </summary>
    public int priority;

    /// <summary>
    /// 需要匹配的设备厂商 ID，值为 0 时忽略。
    /// </summary>
    public int vendorId;

    /// <summary>
    /// 需要匹配的设备产品 ID，值为 0 时忽略。
    /// </summary>
    public int productId;

    /// <summary>
    /// 设备布局名称需要包含的文本。
    /// </summary>
    public string layoutContains;

    /// <summary>
    /// 设备接口名称需要包含的文本。
    /// </summary>
    public string interfaceContains;

    /// <summary>
    /// 设备制造商名称需要包含的文本。
    /// </summary>
    public string manufacturerContains;

    /// <summary>
    /// 设备产品名称需要包含的文本。
    /// </summary>
    public string productContains;

    /// <summary>
    /// 设备名称需要包含的文本。
    /// </summary>
    public string deviceNameContains;

    /// <summary>
    /// 控制方案名称需要包含的文本。
    /// </summary>
    public string controlSchemeContains;
}

/// <summary>
/// 描述一个输入控制路径到图标资源的映射。
/// </summary>
[Serializable]
public sealed class InputGlyphMapping
{
    /// <summary>
    /// 可匹配到该图标的 Unity Input System 控制路径列表。
    /// </summary>
    public string[] controlPaths = Array.Empty<string>();

    /// <summary>
    /// UI 显示时使用的 Sprite 资源。
    /// </summary>
    public Sprite sprite;

    /// <summary>
    /// TextMeshPro Sprite 图集中的 Sprite 名称，未设置时使用 Sprite 资源名称。
    /// </summary>
    public string tmpSpriteName;

    [SerializeField, HideInInspector] private string glyphKey;

    internal string LegacyGlyphKey
    {
        get
        {
            return glyphKey;
        }
    }
}

/// <summary>
/// 描述一种输入设备图标配置档案。
/// </summary>
[Serializable]
public sealed class InputDeviceProfileConfig
{
    /// <summary>
    /// 配置档案 ID。
    /// </summary>
    public string profileId;

    /// <summary>
    /// 当前档案找不到图标时依次查找的回退档案 ID。
    /// </summary>
    public string[] fallbackProfileIds = Array.Empty<string>();

    /// <summary>
    /// 用于匹配 InputAction 绑定分组的提示文本。
    /// </summary>
    public string[] bindingGroupHints = Array.Empty<string>();

    /// <summary>
    /// 用于将设备信息解析到该档案的匹配器列表。
    /// </summary>
    public InputDeviceMatcher[] matchers = Array.Empty<InputDeviceMatcher>();

    /// <summary>
    /// 该档案下的输入控制路径和图标映射列表。
    /// </summary>
    public InputGlyphMapping[] glyphs = Array.Empty<InputGlyphMapping>();

    [NonSerialized] internal int[] GlyphHashBySlot = Array.Empty<int>();
    [NonSerialized] internal string[] GlyphKeyBySlot = Array.Empty<string>();
    [NonSerialized] internal InputGlyphMapping[] GlyphBySlot = Array.Empty<InputGlyphMapping>();
    [NonSerialized] internal int GlyphSlotMask;
}

/// <summary>
/// 输入图标数据库，负责设备档案解析和图标资源查询。
/// </summary>
public sealed class InputGlyphDatabase : ScriptableObject
{
    private const int InitialProfileCapacity = 8;
    private const int InitialGlyphCapacity = 32;
    private const int DefaultProfileCount = 6;
    private const int MaxFallbackDepth = 8;

    private static readonly string[] DefaultProfileIds =
    {
        InputGlyphProfileIds.KeyboardMouse,
        InputGlyphProfileIds.GenericGamepad,
        InputGlyphProfileIds.Xbox,
        InputGlyphProfileIds.PlayStation,
        InputGlyphProfileIds.Switch,
        InputGlyphProfileIds.SteamDeck,
    };

    /// <summary>
    /// 找不到指定图标时使用的占位 Sprite。
    /// </summary>
    public Sprite placeholderSprite;
    [SerializeField, HideInInspector] private InputDeviceProfileConfig[] profiles = Array.Empty<InputDeviceProfileConfig>();

    private InputDeviceProfileConfig[] _profileBySlot = new InputDeviceProfileConfig[InitialProfileCapacity];
    private int[] _profileHashBySlot = new int[InitialProfileCapacity];
    private int _profileSlotMask = InitialProfileCapacity - 1;
    private bool _cacheBuilt;

    /// <summary>
    /// 当前数据库中的配置档案数量。
    /// </summary>
    public int ProfileCount
    {
        get
        {
            return profiles != null ? profiles.Length : 0;
        }
    }

    private void OnEnable()
    {
        EnsureDefaultProfiles();
        BuildCache();
    }

#if UNITY_EDITOR
    private void Reset()
    {
        EditorEnsureDefaultProfiles();
    }

    private void OnValidate()
    {
        EditorEnsureDefaultProfiles();
    }

    /// <summary>
    /// 在编辑器中确保默认配置档案存在，并重建缓存。
    /// </summary>
    /// <returns>默认配置档案内容发生变化时返回 true，否则返回 false。</returns>
    public bool EditorEnsureDefaultProfiles()
    {
        bool changed = EnsureDefaultProfiles();
        BuildCache();
        return changed;
    }

    /// <summary>
    /// 在编辑器中手动重建配置档案和图标缓存。
    /// </summary>
    public void EditorRefreshCache()
    {
        BuildCache();
    }
#endif

    /// <summary>
    /// 根据设备标识、控制方案和设备文本信息尝试解析最匹配的设备配置档案。
    /// </summary>
    /// <param name="vendorId">设备厂商 ID。</param>
    /// <param name="productId">设备产品 ID。</param>
    /// <param name="controlScheme">当前输入控制方案名称。</param>
    /// <param name="deviceName">设备名称。</param>
    /// <param name="layout">设备布局名称。</param>
    /// <param name="interfaceName">设备接口名称。</param>
    /// <param name="manufacturer">设备制造商名称。</param>
    /// <param name="product">设备产品名称。</param>
    /// <param name="profile">输出匹配到的设备配置档案；未匹配时输出键鼠默认档案或 null。</param>
    /// <returns>成功解析到设备配置档案时返回 true，否则返回 false。</returns>
    public bool TryResolveProfile(
        int vendorId,
        int productId,
        string controlScheme,
        string deviceName,
        string layout,
        string interfaceName,
        string manufacturer,
        string product,
        out InputDeviceProfileConfig profile)
    {
        EnsureCache();
        profile = null;

        int bestScore = 0;
        InputDeviceProfileConfig bestProfile = null;
        int count = profiles != null ? profiles.Length : 0;
        for (int i = 0; i < count; i++)
        {
            InputDeviceProfileConfig candidate = profiles[i];
            int score = ScoreProfile(
                candidate,
                vendorId,
                productId,
                controlScheme,
                deviceName,
                layout,
                interfaceName,
                manufacturer,
                product);

            if (score > bestScore)
            {
                bestScore = score;
                bestProfile = candidate;
            }
        }

        if (bestProfile != null)
        {
            profile = bestProfile;
            return true;
        }

        return TryGetProfile(InputGlyphProfileIds.KeyboardMouse, out profile);
    }

    /// <summary>
    /// 根据设备标识、控制方案和设备文本信息解析最匹配的配置档案 ID。
    /// </summary>
    /// <param name="vendorId">设备厂商 ID。</param>
    /// <param name="productId">设备产品 ID。</param>
    /// <param name="controlScheme">当前输入控制方案名称。</param>
    /// <param name="deviceName">设备名称。</param>
    /// <param name="layout">设备布局名称。</param>
    /// <param name="interfaceName">设备接口名称。</param>
    /// <param name="manufacturer">设备制造商名称。</param>
    /// <param name="product">设备产品名称。</param>
    /// <returns>匹配到的配置档案 ID；未匹配时返回键鼠默认档案 ID。</returns>
    public string ResolveProfileId(
        int vendorId,
        int productId,
        string controlScheme,
        string deviceName,
        string layout,
        string interfaceName,
        string manufacturer,
        string product)
    {
        return TryResolveProfile(
            vendorId,
            productId,
            controlScheme,
            deviceName,
            layout,
            interfaceName,
            manufacturer,
            product,
            out InputDeviceProfileConfig profile)
            ? profile.profileId
            : InputGlyphProfileIds.KeyboardMouse;
    }

    /// <summary>
    /// 根据配置档案 ID 尝试获取配置档案。
    /// </summary>
    /// <param name="profileId">要查询的配置档案 ID。</param>
    /// <param name="profile">输出匹配到的设备配置档案，查询失败时为 null。</param>
    /// <returns>成功找到配置档案时返回 true，否则返回 false。</returns>
    public bool TryGetProfile(string profileId, out InputDeviceProfileConfig profile)
    {
        EnsureCache();
        profile = null;
        int hash = InputGlyphStringUtility.StableHash(profileId);
        if (hash == 0 || _profileBySlot == null || _profileBySlot.Length == 0)
        {
            return false;
        }

        int slot = hash & _profileSlotMask;
        int startSlot = slot;
        do
        {
            InputDeviceProfileConfig candidate = _profileBySlot[slot];
            if (candidate == null)
            {
                return false;
            }

            if (_profileHashBySlot[slot] == hash && InputGlyphStringUtility.EqualsIgnoreCase(candidate.profileId, profileId))
            {
                profile = candidate;
                return true;
            }

            slot = (slot + 1) & _profileSlotMask;
        }
        while (slot != startSlot);

        return false;
    }

    /// <summary>
    /// 根据图标键和配置档案 ID 尝试获取 UI Sprite。
    /// </summary>
    /// <param name="glyphKey">图标数据库使用的规范化图标键。</param>
    /// <param name="profileId">用于查询图标的输入设备配置档案 ID。</param>
    /// <param name="sprite">输出匹配到的 UI Sprite；未匹配但存在占位 Sprite 时输出占位 Sprite。</param>
    /// <returns>成功获取匹配 Sprite 或占位 Sprite 时返回 true，否则返回 false。</returns>
    public bool TryGetSprite(string glyphKey, string profileId, out Sprite sprite)
    {
        sprite = null;
        if (string.IsNullOrEmpty(glyphKey))
        {
            sprite = placeholderSprite;
            return sprite != null;
        }

        int glyphHash = InputGlyphStringUtility.StableHashLowerAscii(glyphKey);
        if (TryGetSpriteByHash(glyphKey, glyphHash, profileId, out sprite))
        {
            return true;
        }

        sprite = placeholderSprite;
        return sprite != null;
    }

    /// <summary>
    /// 根据图标键和配置档案 ID 尝试获取 TextMeshPro Sprite 名称及对应 Sprite。
    /// </summary>
    /// <param name="glyphKey">图标数据库使用的规范化图标键。</param>
    /// <param name="profileId">用于查询图标的输入设备配置档案 ID。</param>
    /// <param name="spriteName">输出 TextMeshPro Sprite 图集中的 Sprite 名称，查询失败时为 null。</param>
    /// <param name="sprite">输出 Sprite 资源；映射未设置资源时可能为 null。</param>
    /// <returns>成功获取 TextMeshPro Sprite 名称时返回 true，否则返回 false。</returns>
    public bool TryGetTMPName(string glyphKey, string profileId, out string spriteName, out Sprite sprite)
    {
        spriteName = null;
        sprite = null;
        if (string.IsNullOrEmpty(glyphKey))
        {
            return false;
        }

        int glyphHash = InputGlyphStringUtility.StableHashLowerAscii(glyphKey);
        return TryGetTMPNameByHash(glyphKey, glyphHash, profileId, out spriteName, out sprite);
    }

    private bool TryGetSpriteByHash(string glyphKey, int glyphHash, string profileId, out Sprite sprite)
    {
        sprite = null;
        if (!TryGetProfile(profileId, out InputDeviceProfileConfig profile))
        {
            return false;
        }

        if (TryGetSpriteInProfile(profile, glyphKey, glyphHash, out sprite))
        {
            return true;
        }

        return TryGetSpriteFromFallbacks(profile, glyphKey, glyphHash, out sprite);
    }

    private bool TryGetTMPNameByHash(string glyphKey, int glyphHash, string profileId, out string spriteName, out Sprite sprite)
    {
        spriteName = null;
        sprite = null;
        if (!TryGetProfile(profileId, out InputDeviceProfileConfig profile))
        {
            return false;
        }

        if (TryGetTMPNameInProfile(profile, glyphKey, glyphHash, out spriteName, out sprite))
        {
            return true;
        }

        return TryGetTMPNameFromFallbacks(profile, glyphKey, glyphHash, out spriteName, out sprite);
    }

    private bool TryGetSpriteFromFallbacks(InputDeviceProfileConfig profile, string glyphKey, int glyphHash, out Sprite sprite)
    {
        sprite = null;
        if (profile == null || profile.fallbackProfileIds == null)
        {
            return false;
        }

        string[] currentFallbacks = profile.fallbackProfileIds;
        int depth = 0;
        while (currentFallbacks != null && depth < MaxFallbackDepth)
        {
            for (int i = 0; i < currentFallbacks.Length; i++)
            {
                if (!TryGetProfile(currentFallbacks[i], out InputDeviceProfileConfig fallbackProfile))
                {
                    continue;
                }

                if (TryGetSpriteInProfile(fallbackProfile, glyphKey, glyphHash, out sprite))
                {
                    return true;
                }

                if (fallbackProfile.fallbackProfileIds != null && fallbackProfile.fallbackProfileIds.Length > 0)
                {
                    currentFallbacks = fallbackProfile.fallbackProfileIds;
                    depth++;
                    i = -1;
                    break;
                }
            }

            break;
        }

        return false;
    }

    private bool TryGetTMPNameFromFallbacks(
        InputDeviceProfileConfig profile,
        string glyphKey,
        int glyphHash,
        out string spriteName,
        out Sprite sprite)
    {
        spriteName = null;
        sprite = null;
        if (profile == null || profile.fallbackProfileIds == null)
        {
            return false;
        }

        string[] currentFallbacks = profile.fallbackProfileIds;
        int depth = 0;
        while (currentFallbacks != null && depth < MaxFallbackDepth)
        {
            for (int i = 0; i < currentFallbacks.Length; i++)
            {
                if (!TryGetProfile(currentFallbacks[i], out InputDeviceProfileConfig fallbackProfile))
                {
                    continue;
                }

                if (TryGetTMPNameInProfile(fallbackProfile, glyphKey, glyphHash, out spriteName, out sprite))
                {
                    return true;
                }

                if (fallbackProfile.fallbackProfileIds != null && fallbackProfile.fallbackProfileIds.Length > 0)
                {
                    currentFallbacks = fallbackProfile.fallbackProfileIds;
                    depth++;
                    i = -1;
                    break;
                }
            }

            break;
        }

        return false;
    }

    private static bool TryGetSpriteInProfile(InputDeviceProfileConfig profile, string glyphKey, int glyphHash, out Sprite sprite)
    {
        sprite = null;
        if (!TryGetMappingInProfile(profile, glyphKey, glyphHash, out InputGlyphMapping mapping) || mapping.sprite == null)
        {
            return false;
        }

        sprite = mapping.sprite;
        return true;
    }

    private static bool TryGetTMPNameInProfile(
        InputDeviceProfileConfig profile,
        string glyphKey,
        int glyphHash,
        out string spriteName,
        out Sprite sprite)
    {
        spriteName = null;
        sprite = null;
        if (!TryGetMappingInProfile(profile, glyphKey, glyphHash, out InputGlyphMapping mapping))
        {
            return false;
        }

        spriteName = string.IsNullOrEmpty(mapping.tmpSpriteName)
            ? (mapping.sprite != null ? mapping.sprite.name : null)
            : mapping.tmpSpriteName;
        sprite = mapping.sprite;
        return !string.IsNullOrEmpty(spriteName);
    }

    private static bool TryGetMappingInProfile(
        InputDeviceProfileConfig profile,
        string glyphKey,
        int glyphHash,
        out InputGlyphMapping mapping)
    {
        mapping = null;
        if (profile == null
            || profile.GlyphBySlot == null
            || profile.GlyphHashBySlot == null
            || profile.GlyphBySlot.Length == 0
            || glyphHash == 0)
        {
            return false;
        }

        int slot = glyphHash & profile.GlyphSlotMask;
        int startSlot = slot;
        do
        {
            InputGlyphMapping candidate = profile.GlyphBySlot[slot];
            if (candidate == null)
            {
                return false;
            }

            if (profile.GlyphHashBySlot[slot] == glyphHash
                && InputGlyphStringUtility.EqualsIgnoreCase(profile.GlyphKeyBySlot[slot], glyphKey))
            {
                mapping = candidate;
                return true;
            }

            slot = (slot + 1) & profile.GlyphSlotMask;
        }
        while (slot != startSlot);

        return false;
    }

    private void EnsureCache()
    {
        if (!_cacheBuilt)
        {
            EnsureDefaultProfiles();
            BuildCache();
        }
    }

    private void BuildCache()
    {
        int count = profiles != null ? profiles.Length : 0;
        int capacity = NextPowerOfTwo(Mathf.Max(InitialProfileCapacity, count << 1));
        if (_profileBySlot == null || _profileBySlot.Length != capacity)
        {
            _profileBySlot = new InputDeviceProfileConfig[capacity];
            _profileHashBySlot = new int[capacity];
        }
        else
        {
            Array.Clear(_profileBySlot, 0, _profileBySlot.Length);
            Array.Clear(_profileHashBySlot, 0, _profileHashBySlot.Length);
        }

        _profileSlotMask = capacity - 1;
        for (int i = 0; i < count; i++)
        {
            InputDeviceProfileConfig profile = profiles[i];
            if (profile == null || string.IsNullOrWhiteSpace(profile.profileId))
            {
                continue;
            }

            RegisterProfile(profile);
            BuildGlyphCache(profile);
        }

        _cacheBuilt = true;
    }

    private void RegisterProfile(InputDeviceProfileConfig profile)
    {
        int hash = InputGlyphStringUtility.StableHash(profile.profileId);
        if (hash == 0)
        {
            return;
        }

        int slot = hash & _profileSlotMask;
        int startSlot = slot;
        do
        {
            if (_profileBySlot[slot] == null)
            {
                _profileBySlot[slot] = profile;
                _profileHashBySlot[slot] = hash;
                return;
            }

            if (_profileHashBySlot[slot] == hash
                && InputGlyphStringUtility.EqualsIgnoreCase(_profileBySlot[slot].profileId, profile.profileId))
            {
                _profileBySlot[slot] = profile;
                return;
            }

            slot = (slot + 1) & _profileSlotMask;
        }
        while (slot != startSlot);
    }

    private static int ScoreProfile(
        InputDeviceProfileConfig profile,
        int vendorId,
        int productId,
        string controlScheme,
        string deviceName,
        string layout,
        string interfaceName,
        string manufacturer,
        string product)
    {
        if (profile == null || profile.matchers == null)
        {
            return 0;
        }

        int bestScore = 0;
        for (int i = 0; i < profile.matchers.Length; i++)
        {
            InputDeviceMatcher matcher = profile.matchers[i];
            int score = ScoreMatcher(
                matcher,
                vendorId,
                productId,
                controlScheme,
                deviceName,
                layout,
                interfaceName,
                manufacturer,
                product);
            if (score > bestScore)
            {
                bestScore = score;
            }
        }

        return bestScore;
    }

    private static int ScoreMatcher(
        InputDeviceMatcher matcher,
        int vendorId,
        int productId,
        string controlScheme,
        string deviceName,
        string layout,
        string interfaceName,
        string manufacturer,
        string product)
    {
        if (matcher == null)
        {
            return 0;
        }

        int score = matcher.priority;
        if (matcher.vendorId != 0)
        {
            if (vendorId != matcher.vendorId)
            {
                return 0;
            }

            score += 1000;
        }

        if (matcher.productId != 0)
        {
            if (productId != matcher.productId)
            {
                return 0;
            }

            score += 300;
        }

        if (!MatchContains(controlScheme, matcher.controlSchemeContains, 120, ref score))
        {
            return 0;
        }

        if (!MatchContains(layout, matcher.layoutContains, 90, ref score))
        {
            return 0;
        }

        if (!MatchContains(interfaceName, matcher.interfaceContains, 90, ref score))
        {
            return 0;
        }

        if (!MatchContains(manufacturer, matcher.manufacturerContains, 80, ref score))
        {
            return 0;
        }

        if (!MatchContains(product, matcher.productContains, 70, ref score))
        {
            return 0;
        }

        if (!MatchContains(deviceName, matcher.deviceNameContains, 70, ref score))
        {
            return 0;
        }

        return score;
    }

    private static bool MatchContains(string source, string value, int weight, ref int score)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (!InputGlyphStringUtility.ContainsIgnoreCase(source, value))
        {
            return false;
        }

        score += weight;
        return true;
    }

    private static void BuildGlyphCache(InputDeviceProfileConfig profile)
    {
        int glyphCount = profile.glyphs != null ? profile.glyphs.Length : 0;
        int keyCount = CountGlyphKeys(profile.glyphs, glyphCount);
        int capacity = NextPowerOfTwo(Mathf.Max(InitialGlyphCapacity, keyCount << 1));
        if (profile.GlyphBySlot == null || profile.GlyphBySlot.Length != capacity)
        {
            profile.GlyphBySlot = new InputGlyphMapping[capacity];
            profile.GlyphHashBySlot = new int[capacity];
            profile.GlyphKeyBySlot = new string[capacity];
        }
        else
        {
            Array.Clear(profile.GlyphBySlot, 0, profile.GlyphBySlot.Length);
            Array.Clear(profile.GlyphHashBySlot, 0, profile.GlyphHashBySlot.Length);
            if (profile.GlyphKeyBySlot == null || profile.GlyphKeyBySlot.Length != capacity)
            {
                profile.GlyphKeyBySlot = new string[capacity];
            }
            else
            {
                Array.Clear(profile.GlyphKeyBySlot, 0, profile.GlyphKeyBySlot.Length);
            }
        }

        profile.GlyphSlotMask = capacity - 1;
        for (int i = 0; i < glyphCount; i++)
        {
            InputGlyphMapping mapping = profile.glyphs[i];
            if (mapping == null)
            {
                continue;
            }

            RegisterGlyph(profile, mapping);
        }
    }

    private static int CountGlyphKeys(InputGlyphMapping[] glyphs, int glyphCount)
    {
        int count = 0;
        for (int i = 0; i < glyphCount; i++)
        {
            InputGlyphMapping mapping = glyphs[i];
            if (mapping == null)
            {
                continue;
            }

            bool hasControlPath = false;
            if (mapping.controlPaths != null)
            {
                for (int pathIndex = 0; pathIndex < mapping.controlPaths.Length; pathIndex++)
                {
                    if (!string.IsNullOrWhiteSpace(mapping.controlPaths[pathIndex]))
                    {
                        count++;
                        hasControlPath = true;
                    }
                }
            }

            if (!hasControlPath && !string.IsNullOrWhiteSpace(mapping.LegacyGlyphKey))
            {
                count++;
            }
        }

        return count;
    }

    private static void RegisterGlyph(InputDeviceProfileConfig profile, InputGlyphMapping mapping)
    {
        bool registered = false;
        if (mapping.controlPaths != null)
        {
            for (int i = 0; i < mapping.controlPaths.Length; i++)
            {
                registered |= RegisterGlyphPath(profile, mapping, mapping.controlPaths[i]);
            }
        }

        if (!registered)
        {
            RegisterGlyphKey(profile, mapping, mapping.LegacyGlyphKey);
        }
    }

    private static bool RegisterGlyphPath(InputDeviceProfileConfig profile, InputGlyphMapping mapping, string controlPath)
    {
        string glyphKey = InputGlyphService.GetGlyphKeyFromControlPath(controlPath);
        return RegisterGlyphKey(profile, mapping, glyphKey);
    }

    private static bool RegisterGlyphKey(InputDeviceProfileConfig profile, InputGlyphMapping mapping, string glyphKey)
    {
        if (string.IsNullOrWhiteSpace(glyphKey))
        {
            return false;
        }

        int hash = InputGlyphStringUtility.StableHashLowerAscii(glyphKey);
        if (hash == 0)
        {
            return false;
        }

        int slot = hash & profile.GlyphSlotMask;
        int startSlot = slot;
        do
        {
            if (profile.GlyphBySlot[slot] == null)
            {
                profile.GlyphBySlot[slot] = mapping;
                profile.GlyphHashBySlot[slot] = hash;
                profile.GlyphKeyBySlot[slot] = glyphKey;
                return true;
            }

            if (profile.GlyphHashBySlot[slot] == hash
                && InputGlyphStringUtility.EqualsIgnoreCase(profile.GlyphKeyBySlot[slot], glyphKey))
            {
                profile.GlyphBySlot[slot] = mapping;
                profile.GlyphKeyBySlot[slot] = glyphKey;
                return true;
            }

            slot = (slot + 1) & profile.GlyphSlotMask;
        }
        while (slot != startSlot);

        return false;
    }

    private bool EnsureDefaultProfiles()
    {
        bool changed = false;
        InputDeviceProfileConfig[] fixedProfiles = new InputDeviceProfileConfig[DefaultProfileCount];
        for (int i = 0; i < DefaultProfileIds.Length; i++)
        {
            int oldIndex = FindProfileIndex(DefaultProfileIds[i]);
            InputDeviceProfileConfig profile = oldIndex >= 0 ? profiles[oldIndex] : null;
            if (profile == null)
            {
                profile = CreateDefaultProfile(DefaultProfileIds[i]);
                changed = true;
            }

            changed |= EnsureProfileDefaults(profile, DefaultProfileIds[i]);
            fixedProfiles[i] = profile;
        }

        if (!ProfilesMatchFixedSet(fixedProfiles))
        {
            profiles = fixedProfiles;
            changed = true;
        }

        _cacheBuilt = false;
        return changed;
    }

    private bool ProfilesMatchFixedSet(InputDeviceProfileConfig[] fixedProfiles)
    {
        if (profiles == null || profiles.Length != DefaultProfileCount)
        {
            return false;
        }

        for (int i = 0; i < DefaultProfileCount; i++)
        {
            if (!ReferenceEquals(profiles[i], fixedProfiles[i]))
            {
                return false;
            }
        }

        return true;
    }

    private int FindProfileIndex(string profileId)
    {
        if (profiles == null)
        {
            return -1;
        }

        for (int i = 0; i < profiles.Length; i++)
        {
            InputDeviceProfileConfig profile = profiles[i];
            if (profile != null && InputGlyphStringUtility.EqualsIgnoreCase(profile.profileId, profileId))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool EnsureProfileDefaults(InputDeviceProfileConfig profile, string fixedProfileId)
    {
        bool changed = false;
        if (!InputGlyphStringUtility.EqualsOrdinal(profile.profileId, fixedProfileId))
        {
            profile.profileId = fixedProfileId;
            changed = true;
        }

        string[] fallbackProfileIds = GetDefaultFallbackProfileIds(fixedProfileId);
        if (!StringArraysEqual(profile.fallbackProfileIds, fallbackProfileIds))
        {
            profile.fallbackProfileIds = fallbackProfileIds;
            changed = true;
        }

        string[] bindingGroupHints = GetDefaultBindingGroupHints(fixedProfileId);
        if (!StringArraysEqual(profile.bindingGroupHints, bindingGroupHints))
        {
            profile.bindingGroupHints = bindingGroupHints;
            changed = true;
        }

        InputDeviceMatcher[] matchers = CreateDefaultMatchers(fixedProfileId);
        if (!MatchersEqual(profile.matchers, matchers))
        {
            profile.matchers = matchers;
            changed = true;
        }

        if (profile.glyphs == null)
        {
            profile.glyphs = Array.Empty<InputGlyphMapping>();
            changed = true;
        }

        return changed;
    }

    private static InputDeviceProfileConfig CreateDefaultProfile(string profileId)
    {
        InputDeviceProfileConfig profile = new InputDeviceProfileConfig();
        EnsureProfileDefaults(profile, profileId);
        return profile;
    }

    private static string[] GetDefaultFallbackProfileIds(string profileId)
    {
        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.KeyboardMouse)
            || InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.GenericGamepad))
        {
            return Array.Empty<string>();
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.Switch))
        {
            return new[] { InputGlyphProfileIds.GenericGamepad, InputGlyphProfileIds.Xbox };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.SteamDeck))
        {
            return new[] { InputGlyphProfileIds.Xbox, InputGlyphProfileIds.GenericGamepad };
        }

        return new[] { InputGlyphProfileIds.GenericGamepad };
    }

    private static string[] GetDefaultBindingGroupHints(string profileId)
    {
        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.KeyboardMouse))
        {
            return new[] { "keyboard", "mouse", "keyboard&mouse", "keyboardmouse", "kbm" };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.Xbox))
        {
            return new[] { "xbox", "xinput", "gamepad", "controller" };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.PlayStation))
        {
            return new[] { "playstation", "dualshock", "dualsense", "gamepad", "controller" };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.Switch))
        {
            return new[] { "switch", "nintendo", "joy-con", "joycon", "gamepad", "controller" };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.SteamDeck))
        {
            return new[] { "steamdeck", "steam", "xbox", "xinput", "gamepad", "controller" };
        }

        return new[] { "gamepad", "controller", "joystick" };
    }

    private static InputDeviceMatcher[] CreateDefaultMatchers(string profileId)
    {
        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.KeyboardMouse))
        {
            return new[]
            {
                new InputDeviceMatcher { priority = 100, controlSchemeContains = "keyboard" },
                new InputDeviceMatcher { priority = 100, controlSchemeContains = "mouse" },
                new InputDeviceMatcher { priority = 90, layoutContains = "keyboard" },
                new InputDeviceMatcher { priority = 90, layoutContains = "mouse" },
            };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.GenericGamepad))
        {
            return new[]
            {
                new InputDeviceMatcher { priority = 40, controlSchemeContains = "gamepad" },
                new InputDeviceMatcher { priority = 35, layoutContains = "gamepad" },
                new InputDeviceMatcher { priority = 35, layoutContains = "controller" },
                new InputDeviceMatcher { priority = 35, layoutContains = "joystick" },
            };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.Xbox))
        {
            return new[]
            {
                new InputDeviceMatcher { priority = 100, vendorId = 0x045E },
                new InputDeviceMatcher { priority = 100, vendorId = 1118 },
                new InputDeviceMatcher { priority = 90, interfaceContains = "xinput" },
                new InputDeviceMatcher { priority = 80, deviceNameContains = "xbox" },
                new InputDeviceMatcher { priority = 75, productContains = "xbox" },
            };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.PlayStation))
        {
            return new[]
            {
                new InputDeviceMatcher { priority = 100, vendorId = 0x054C },
                new InputDeviceMatcher { priority = 100, vendorId = 1356 },
                new InputDeviceMatcher { priority = 90, layoutContains = "dualsense" },
                new InputDeviceMatcher { priority = 90, layoutContains = "dualshock" },
                new InputDeviceMatcher { priority = 80, deviceNameContains = "dualsense" },
                new InputDeviceMatcher { priority = 80, deviceNameContains = "dualshock" },
                new InputDeviceMatcher { priority = 75, productContains = "playstation" },
            };
        }

        if (InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.Switch))
        {
            return new[]
            {
                new InputDeviceMatcher { priority = 100, vendorId = 0x057E },
                new InputDeviceMatcher { priority = 100, vendorId = 1406 },
                new InputDeviceMatcher { priority = 90, manufacturerContains = "nintendo" },
                new InputDeviceMatcher { priority = 85, deviceNameContains = "switch" },
                new InputDeviceMatcher { priority = 85, deviceNameContains = "joy-con" },
                new InputDeviceMatcher { priority = 85, deviceNameContains = "joycon" },
            };
        }

        return new[]
        {
            new InputDeviceMatcher { priority = 100, manufacturerContains = "valve" },
            new InputDeviceMatcher { priority = 100, deviceNameContains = "steam deck" },
            new InputDeviceMatcher { priority = 100, productContains = "steam deck" },
            new InputDeviceMatcher { priority = 80, deviceNameContains = "steam" },
        };
    }

    private static bool EnsureGlyphDefaults(InputDeviceProfileConfig profile, string profileId)
    {
        InputGlyphMapping[] defaults = InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.KeyboardMouse)
            ? CreateKeyboardMouseGlyphMappings()
            : CreateGamepadGlyphMappings();
        bool changed = false;
        bool keyboardMouseProfile = InputGlyphStringUtility.EqualsOrdinal(profileId, InputGlyphProfileIds.KeyboardMouse);
        for (int i = 0; i < profile.glyphs.Length; i++)
        {
            InputGlyphMapping mapping = profile.glyphs[i];
            if (mapping == null)
            {
                continue;
            }

            if ((mapping.controlPaths == null || mapping.controlPaths.Length == 0)
                && !string.IsNullOrWhiteSpace(mapping.LegacyGlyphKey))
            {
                mapping.controlPaths = CreateControlPathsFromGlyphKey(mapping.LegacyGlyphKey);
                changed = true;
            }

            changed |= FilterMappingPaths(mapping, keyboardMouseProfile);
        }

        int writeIndex = 0;
        for (int i = 0; i < profile.glyphs.Length; i++)
        {
            InputGlyphMapping mapping = profile.glyphs[i];
            if (HasAllowedMappingPath(mapping, keyboardMouseProfile))
            {
                profile.glyphs[writeIndex] = mapping;
                writeIndex++;
            }
        }

        if (writeIndex != profile.glyphs.Length)
        {
            Array.Resize(ref profile.glyphs, writeIndex);
            changed = true;
        }

        for (int i = 0; i < defaults.Length; i++)
        {
            if (!ContainsGlyphMapping(profile.glyphs, defaults[i]))
            {
                AppendGlyphMapping(ref profile.glyphs, defaults[i]);
                changed = true;
            }
        }

        return changed;
    }

    private static bool FilterMappingPaths(InputGlyphMapping mapping, bool keyboardMouseProfile)
    {
        if (mapping == null || mapping.controlPaths == null)
        {
            return false;
        }

        int writeIndex = 0;
        for (int i = 0; i < mapping.controlPaths.Length; i++)
        {
            string controlPath = mapping.controlPaths[i];
            if (IsAllowedControlPath(controlPath, keyboardMouseProfile))
            {
                mapping.controlPaths[writeIndex] = controlPath;
                writeIndex++;
            }
        }

        if (writeIndex == mapping.controlPaths.Length)
        {
            return false;
        }

        Array.Resize(ref mapping.controlPaths, writeIndex);
        return true;
    }

    private static bool HasAllowedMappingPath(InputGlyphMapping mapping, bool keyboardMouseProfile)
    {
        if (mapping == null || mapping.controlPaths == null)
        {
            return false;
        }

        for (int i = 0; i < mapping.controlPaths.Length; i++)
        {
            if (IsAllowedControlPath(mapping.controlPaths[i], keyboardMouseProfile))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedControlPath(string controlPath, bool keyboardMouseProfile)
    {
        string key = InputGlyphService.GetGlyphKeyFromControlPath(controlPath);
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (keyboardMouseProfile)
        {
            return InputGlyphStringUtility.StartsWithIgnoreCase(key, "keyboard/")
                   || InputGlyphStringUtility.StartsWithIgnoreCase(key, "mouse/");
        }

        return InputGlyphStringUtility.StartsWithIgnoreCase(key, "gamepad/")
               || InputGlyphStringUtility.StartsWithIgnoreCase(key, "joystick/");
    }

    private static InputGlyphMapping[] CreateKeyboardMouseGlyphMappings()
    {
        return new[]
        {
            CreateGlyph("<Keyboard>/space"),
            CreateGlyph("<Keyboard>/enter", "<Keyboard>/numpadEnter"),
            CreateGlyph("<Keyboard>/escape"),
            CreateGlyph("<Keyboard>/tab"),
            CreateGlyph("<Keyboard>/backspace"),
            CreateGlyph("<Keyboard>/delete"),
            CreateGlyph("<Keyboard>/leftCtrl", "<Keyboard>/rightCtrl", "<Keyboard>/ctrl"),
            CreateGlyph("<Keyboard>/leftShift", "<Keyboard>/rightShift", "<Keyboard>/shift"),
            CreateGlyph("<Keyboard>/leftAlt", "<Keyboard>/rightAlt", "<Keyboard>/alt"),
            CreateGlyph("<Keyboard>/leftArrow"),
            CreateGlyph("<Keyboard>/rightArrow"),
            CreateGlyph("<Keyboard>/upArrow"),
            CreateGlyph("<Keyboard>/downArrow"),
            CreateGlyph("<Keyboard>/w"),
            CreateGlyph("<Keyboard>/a"),
            CreateGlyph("<Keyboard>/s"),
            CreateGlyph("<Keyboard>/d"),
            CreateGlyph("<Mouse>/leftButton"),
            CreateGlyph("<Mouse>/rightButton"),
            CreateGlyph("<Mouse>/middleButton"),
            CreateGlyph("<Mouse>/scroll/up"),
            CreateGlyph("<Mouse>/scroll/down"),
        };
    }

    private static InputGlyphMapping[] CreateGamepadGlyphMappings()
    {
        return new[]
        {
            CreateGlyph("<Gamepad>/buttonSouth"),
            CreateGlyph("<Gamepad>/buttonEast"),
            CreateGlyph("<Gamepad>/buttonWest"),
            CreateGlyph("<Gamepad>/buttonNorth"),
            CreateGlyph("<Gamepad>/leftTrigger"),
            CreateGlyph("<Gamepad>/rightTrigger"),
            CreateGlyph("<Gamepad>/leftShoulder"),
            CreateGlyph("<Gamepad>/rightShoulder"),
            CreateGlyph("<Gamepad>/leftStick"),
            CreateGlyph("<Gamepad>/rightStick"),
            CreateGlyph("<Gamepad>/startButton"),
            CreateGlyph("<Gamepad>/selectButton"),
            CreateGlyph("<Gamepad>/dpad/up"),
            CreateGlyph("<Gamepad>/dpad/down"),
            CreateGlyph("<Gamepad>/dpad/left"),
            CreateGlyph("<Gamepad>/dpad/right"),
        };
    }

    private static InputGlyphMapping CreateGlyph(params string[] controlPaths)
    {
        return new InputGlyphMapping
        {
            controlPaths = controlPaths,
        };
    }

    private static string[] CreateControlPathsFromGlyphKey(string glyphKey)
    {
        if (InputGlyphStringUtility.ContainsIgnoreCase(glyphKey, "keyboard/"))
        {
            return new[] { BuildControlPathFromGlyphKey("Keyboard", glyphKey, 9) };
        }

        if (InputGlyphStringUtility.ContainsIgnoreCase(glyphKey, "mouse/"))
        {
            return new[] { BuildControlPathFromGlyphKey("Mouse", glyphKey, 6) };
        }

        if (InputGlyphStringUtility.ContainsIgnoreCase(glyphKey, "gamepad/"))
        {
            return new[] { BuildControlPathFromGlyphKey("Gamepad", glyphKey, 8) };
        }

        return new[] { glyphKey };
    }

    private static string BuildControlPathFromGlyphKey(string layout, string glyphKey, int prefixLength)
    {
        using (Utf16ValueStringBuilder sb = ZString.CreateStringBuilder())
        {
            sb.Append('<');
            sb.Append(layout);
            sb.Append(">/");
            for (int i = prefixLength; i < glyphKey.Length; i++)
            {
                sb.Append(glyphKey[i]);
            }

            return sb.ToString();
        }
    }

    private static bool ContainsGlyphMapping(InputGlyphMapping[] glyphs, InputGlyphMapping target)
    {
        string key = GetFirstGlyphKey(target);
        for (int i = 0; i < glyphs.Length; i++)
        {
            if (InputGlyphStringUtility.EqualsIgnoreCase(GetFirstGlyphKey(glyphs[i]), key))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetFirstGlyphKey(InputGlyphMapping mapping)
    {
        if (mapping == null || mapping.controlPaths == null)
        {
            return string.Empty;
        }

        for (int i = 0; i < mapping.controlPaths.Length; i++)
        {
            string key = InputGlyphService.GetGlyphKeyFromControlPath(mapping.controlPaths[i]);
            if (!string.IsNullOrEmpty(key))
            {
                return key;
            }
        }

        return string.Empty;
    }

    private static void AppendGlyphMapping(ref InputGlyphMapping[] glyphs, InputGlyphMapping mapping)
    {
        int length = glyphs != null ? glyphs.Length : 0;
        Array.Resize(ref glyphs, length + 1);
        glyphs[length] = mapping;
    }

    private static bool StringArraysEqual(string[] a, string[] b)
    {
        int aLength = a != null ? a.Length : 0;
        int bLength = b != null ? b.Length : 0;
        if (aLength != bLength)
        {
            return false;
        }

        for (int i = 0; i < aLength; i++)
        {
            if (!InputGlyphStringUtility.EqualsOrdinal(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchersEqual(InputDeviceMatcher[] a, InputDeviceMatcher[] b)
    {
        int aLength = a != null ? a.Length : 0;
        int bLength = b != null ? b.Length : 0;
        if (aLength != bLength)
        {
            return false;
        }

        for (int i = 0; i < aLength; i++)
        {
            if (!MatcherEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatcherEquals(InputDeviceMatcher a, InputDeviceMatcher b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return a.priority == b.priority
               && a.vendorId == b.vendorId
               && a.productId == b.productId
               && InputGlyphStringUtility.EqualsOrdinal(a.layoutContains, b.layoutContains)
               && InputGlyphStringUtility.EqualsOrdinal(a.interfaceContains, b.interfaceContains)
               && InputGlyphStringUtility.EqualsOrdinal(a.manufacturerContains, b.manufacturerContains)
               && InputGlyphStringUtility.EqualsOrdinal(a.productContains, b.productContains)
               && InputGlyphStringUtility.EqualsOrdinal(a.deviceNameContains, b.deviceNameContains)
               && InputGlyphStringUtility.EqualsOrdinal(a.controlSchemeContains, b.controlSchemeContains);
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
