#if INPUTSYSTEM_SUPPORT
using System;
using System.Runtime.CompilerServices;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public static class InputGlyphService
{
    private const int InitialCacheCapacity = 64;
    private static readonly InputGlyphStringMap<string> DisplayNameCache = new InputGlyphStringMap<string>(InitialCacheCapacity);
    private static readonly InputGlyphStringMap<string> GlyphKeyCache = new InputGlyphStringMap<string>(InitialCacheCapacity);
    private static readonly InputGlyphStringMap<string> SpriteTagCache = new InputGlyphStringMap<string>(InitialCacheCapacity);
    private static readonly BindingSelectionMap BindingSelections = new BindingSelectionMap(InitialCacheCapacity);

    private static InputGlyphDatabase _database;

    /// <summary>
    /// 设置当前使用的输入图标数据库，并清空已缓存的绑定选择。
    /// </summary>
    /// <param name="database">要设置为当前全局使用的输入图标数据库，传入 null 表示清除当前数据库。</param>
    public static void SetDatabase(InputGlyphDatabase database)
    {
        _database = database;
        BindingSelections.Clear();
    }

    /// <summary>
    /// 清空输入动作到绑定索引的选择缓存。
    /// </summary>
    public static void ClearBindingCache()
    {
        BindingSelections.Clear();
    }

    /// <summary>
    /// 根据设备信息和控制方案解析输入图标配置档案 ID。
    /// </summary>
    /// <param name="vendorId">设备厂商 ID。</param>
    /// <param name="productId">设备产品 ID。</param>
    /// <param name="controlScheme">当前输入控制方案名称。</param>
    /// <param name="deviceName">设备名称。</param>
    /// <param name="layout">设备布局名称。</param>
    /// <param name="interfaceName">设备接口名称。</param>
    /// <param name="manufacturer">设备制造商名称。</param>
    /// <param name="product">设备产品名称。</param>
    /// <returns>匹配到的输入图标配置档案 ID；未设置数据库时返回键鼠或通用手柄档案 ID。</returns>
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

    /// <summary>
    /// 使用当前输入上下文获取动作对应绑定的控制路径。
    /// </summary>
    /// <param name="action">要查询绑定路径的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <returns>匹配到的有效控制路径；未匹配时返回空字符串。</returns>
    public static string GetBindingControlPath(
        InputAction action,
        string compositePartName = null)
    {
        return GetBindingControlPath(action, compositePartName, InputDeviceWatcher.CurrentContext);
    }

    /// <summary>
    /// 使用指定输入上下文获取动作对应绑定的控制路径。
    /// </summary>
    /// <param name="action">要查询绑定路径的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="context">用于选择绑定和设备档案的输入上下文；传入 null 时使用当前上下文。</param>
    /// <returns>匹配到的有效控制路径；未匹配时返回空字符串。</returns>
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

    /// <summary>
    /// 使用当前输入上下文获取动作引用对应绑定的控制路径。
    /// </summary>
    /// <param name="actionReference">要查询绑定路径的输入动作引用。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <returns>匹配到的有效控制路径；未匹配时返回空字符串。</returns>
    public static string GetBindingControlPath(
        InputActionReference actionReference,
        string compositePartName = null)
    {
        return GetBindingControlPath(actionReference != null ? actionReference.action : null, compositePartName);
    }

    /// <summary>
    /// 使用指定输入上下文获取动作引用对应绑定的控制路径。
    /// </summary>
    /// <param name="actionReference">要查询绑定路径的输入动作引用。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="context">用于选择绑定和设备档案的输入上下文；传入 null 时使用当前上下文。</param>
    /// <returns>匹配到的有效控制路径；未匹配时返回空字符串。</returns>
    public static string GetBindingControlPath(
        InputActionReference actionReference,
        string compositePartName,
        InputGlyphContext context)
    {
        return GetBindingControlPath(actionReference != null ? actionReference.action : null, compositePartName, context);
    }

    /// <summary>
    /// 使用当前输入上下文尝试获取动作绑定对应的 TextMeshPro Sprite 标签。
    /// </summary>
    /// <param name="action">要查询图标标签的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="tag">输出 TextMeshPro Sprite 标签，查询失败时为 null。</param>
    /// <param name="displayFallback">输出可读显示文本，用于没有图标时回退显示。</param>
    /// <param name="db">可选的输入图标数据库；传入 null 时使用当前全局数据库。</param>
    /// <returns>成功生成 TextMeshPro Sprite 标签时返回 true，否则返回 false。</returns>
    public static bool TryGetTMPTagForActionPath(
        InputAction action,
        string compositePartName,
        out string tag,
        out string displayFallback,
        InputGlyphDatabase db = null)
    {
        return TryGetTMPTagForActionPath(action, compositePartName, InputDeviceWatcher.CurrentContext, out tag, out displayFallback, db);
    }

    /// <summary>
    /// 使用指定输入上下文尝试获取动作绑定对应的 TextMeshPro Sprite 标签。
    /// </summary>
    /// <param name="action">要查询图标标签的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="context">用于选择绑定和设备档案的输入上下文；传入 null 时使用当前上下文。</param>
    /// <param name="tag">输出 TextMeshPro Sprite 标签，查询失败时为 null。</param>
    /// <param name="displayFallback">输出可读显示文本，用于没有图标时回退显示。</param>
    /// <param name="db">可选的输入图标数据库；传入 null 时使用当前全局数据库。</param>
    /// <returns>成功生成 TextMeshPro Sprite 标签时返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 使用指定输入上下文尝试获取动作引用对应的 TextMeshPro Sprite 标签。
    /// </summary>
    /// <param name="actionReference">要查询图标标签的输入动作引用。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="context">用于选择绑定和设备档案的输入上下文；传入 null 时使用当前上下文。</param>
    /// <param name="tag">输出 TextMeshPro Sprite 标签，查询失败时为 null。</param>
    /// <param name="displayFallback">输出可读显示文本，用于没有图标时回退显示。</param>
    /// <param name="db">可选的输入图标数据库；传入 null 时使用当前全局数据库。</param>
    /// <returns>成功生成 TextMeshPro Sprite 标签时返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 使用当前输入上下文尝试获取动作绑定对应的 UI Sprite。
    /// </summary>
    /// <param name="action">要查询 UI Sprite 的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="sprite">输出匹配到的 UI Sprite，查询失败时为 null。</param>
    /// <param name="db">可选的输入图标数据库；传入 null 时使用当前全局数据库。</param>
    /// <returns>成功获取 UI Sprite 时返回 true，否则返回 false。</returns>
    public static bool TryGetUISpriteForActionPath(
        InputAction action,
        string compositePartName,
        out Sprite sprite,
        InputGlyphDatabase db = null)
    {
        return TryGetUISpriteForActionPath(action, compositePartName, InputDeviceWatcher.CurrentContext, out sprite, db);
    }

    /// <summary>
    /// 使用指定输入上下文尝试获取动作绑定对应的 UI Sprite。
    /// </summary>
    /// <param name="action">要查询 UI Sprite 的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="context">用于选择绑定和设备档案的输入上下文；传入 null 时使用当前上下文。</param>
    /// <param name="sprite">输出匹配到的 UI Sprite，查询失败时为 null。</param>
    /// <param name="db">可选的输入图标数据库；传入 null 时使用当前全局数据库。</param>
    /// <returns>成功获取 UI Sprite 时返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 使用指定输入上下文尝试获取动作引用对应的 UI Sprite。
    /// </summary>
    /// <param name="actionReference">要查询 UI Sprite 的输入动作引用。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="context">用于选择绑定和设备档案的输入上下文；传入 null 时使用当前上下文。</param>
    /// <param name="sprite">输出匹配到的 UI Sprite，查询失败时为 null。</param>
    /// <param name="db">可选的输入图标数据库；传入 null 时使用当前全局数据库。</param>
    /// <returns>成功获取 UI Sprite 时返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 根据控制路径和配置档案 ID 尝试获取 TextMeshPro Sprite 标签，并提供显示文本回退值。
    /// </summary>
    /// <param name="controlPath">Unity Input System 控制路径。</param>
    /// <param name="profileId">用于查询图标的输入设备配置档案 ID。</param>
    /// <param name="tag">输出 TextMeshPro Sprite 标签，查询失败时为 null。</param>
    /// <param name="displayFallback">输出由控制路径生成的可读显示文本。</param>
    /// <param name="db">可选的输入图标数据库；传入 null 时使用当前全局数据库。</param>
    /// <returns>成功生成 TextMeshPro Sprite 标签时返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 根据控制路径和配置档案 ID 尝试获取 UI Sprite。
    /// </summary>
    /// <param name="controlPath">Unity Input System 控制路径。</param>
    /// <param name="profileId">用于查询图标的输入设备配置档案 ID。</param>
    /// <param name="sprite">输出匹配到的 UI Sprite，查询失败时为 null。</param>
    /// <param name="db">可选的输入图标数据库；传入 null 时使用当前全局数据库。</param>
    /// <returns>成功获取 UI Sprite 时返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 使用当前输入上下文获取输入动作的可读显示名称。
    /// </summary>
    /// <param name="action">要查询显示名称的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <returns>匹配绑定的可读显示名称；未匹配时返回空字符串。</returns>
    public static string GetDisplayNameFromInputAction(
        InputAction action,
        string compositePartName = null)
    {
        return GetDisplayNameFromInputAction(action, compositePartName, InputDeviceWatcher.CurrentContext);
    }

    /// <summary>
    /// 使用指定输入上下文获取输入动作的可读显示名称。
    /// </summary>
    /// <param name="action">要查询显示名称的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="context">用于选择绑定和设备档案的输入上下文；传入 null 时使用当前上下文。</param>
    /// <returns>匹配绑定的可读显示名称；未匹配时返回空字符串。</returns>
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

    /// <summary>
    /// 将输入控制路径转换为可读显示名称。
    /// </summary>
    /// <param name="controlPath">Unity Input System 控制路径。</param>
    /// <returns>控制路径对应的可读显示名称；路径为空或无效时返回空字符串。</returns>
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

    /// <summary>
    /// 尝试按输入上下文和组合按键部分选择最合适的动作绑定。
    /// </summary>
    /// <param name="action">要查询绑定的输入动作。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；非组合绑定传入 null。</param>
    /// <param name="context">用于选择绑定和设备档案的输入上下文；传入 null 时使用当前上下文。</param>
    /// <param name="binding">输出匹配到的输入绑定，失败时为默认值。</param>
    /// <returns>成功找到匹配绑定时返回 true，否则返回 false。</returns>
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

    /// <summary>
    /// 在动作绑定列表中查找与上下文和配置档案最匹配的绑定索引。
    /// </summary>
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

    /// <summary>
    /// 将输入控制路径规范化为图标数据库使用的图标键。
    /// </summary>
    /// <param name="controlPath">Unity Input System 控制路径。</param>
    /// <returns>图标数据库使用的规范化图标键；路径为空或无法解析时返回空字符串。</returns>
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

    /// <summary>
    /// 计算单个绑定与当前输入上下文和配置档案的匹配分数。
    /// </summary>
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

    /// <summary>
    /// 判断绑定分组是否命中配置档案中的分组提示。
    /// </summary>
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

    /// <summary>
    /// 在无法生成可读名称时，从控制路径末段构造显示回退文本。
    /// </summary>
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

    /// <summary>
    /// 查找控制路径最后一个路径段的起始位置。
    /// </summary>
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

    /// <summary>
    /// 判断字符是否需要从显示回退文本两端裁剪。
    /// </summary>
    private static bool IsDisplayTrimChar(char value)
    {
        return value == '{'
               || value == '}'
               || value == '<'
               || value == '>'
               || value == '\''
               || value == '"';
    }

    /// <summary>
    /// 判断控制路径是否匹配当前输入上下文的设备类型或配置档案。
    /// </summary>
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

    /// <summary>
    /// 判断指定字符串片段是否包含任意一个提示文本。
    /// </summary>
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

    /// <summary>
    /// 判断指定字符串片段是否忽略大小写包含目标文本。
    /// </summary>
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

    /// <summary>
    /// 根据 Sprite 或指定名称构造并缓存 TextMeshPro Sprite 标签。
    /// </summary>
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

    /// <summary>
    /// 判断设备信息是否更接近键盘鼠标输入。
    /// </summary>
    private static bool IsKeyboardMouseLike(string controlScheme, string layout, string deviceName)
    {
        return InputGlyphStringUtility.ContainsIgnoreCase(controlScheme, "keyboard")
               || InputGlyphStringUtility.ContainsIgnoreCase(controlScheme, "mouse")
               || InputGlyphStringUtility.ContainsIgnoreCase(layout, "keyboard")
               || InputGlyphStringUtility.ContainsIgnoreCase(layout, "mouse")
               || InputGlyphStringUtility.ContainsIgnoreCase(deviceName, "keyboard")
               || InputGlyphStringUtility.ContainsIgnoreCase(deviceName, "mouse");
    }

    /// <summary>
    /// 解析有效输入上下文，缺省时使用当前设备监听器上下文。
    /// </summary>
    private static InputGlyphContext ResolveContext(InputGlyphContext context)
    {
        return context != null ? context : InputDeviceWatcher.CurrentContext;
    }

    /// <summary>
    /// 获取绑定的有效控制路径，优先使用重绑定后的路径。
    /// </summary>
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

        /// <summary>
        /// 初始化绑定选择缓存表。
        /// </summary>
        /// <param name="capacity">缓存表的初始容量。</param>
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

        /// <summary>
        /// 清空所有缓存的绑定选择记录。
        /// </summary>
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

        /// <summary>
        /// 尝试读取指定动作、组合部分和配置档案对应的缓存绑定索引。
        /// </summary>
        /// <param name="action">用于定位缓存记录的输入动作。</param>
        /// <param name="compositePartName">用于定位缓存记录的组合绑定部分名称。</param>
        /// <param name="profileId">用于定位缓存记录的配置档案 ID。</param>
        /// <param name="bindingIndex">输出缓存的绑定索引，未命中时为 -1。</param>
        /// <returns>找到缓存记录时返回 true，否则返回 false。</returns>
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

        /// <summary>
        /// 写入或更新指定动作、组合部分和配置档案对应的绑定索引。
        /// </summary>
        /// <param name="action">用于定位缓存记录的输入动作。</param>
        /// <param name="compositePartName">用于定位缓存记录的组合绑定部分名称。</param>
        /// <param name="profileId">用于定位缓存记录的配置档案 ID。</param>
        /// <param name="bindingIndex">要缓存的绑定索引。</param>
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

        /// <summary>
        /// 移除指定动作、组合部分和配置档案对应的缓存记录。
        /// </summary>
        /// <param name="action">用于定位缓存记录的输入动作。</param>
        /// <param name="compositePartName">用于定位缓存记录的组合绑定部分名称。</param>
        /// <param name="profileId">用于定位缓存记录的配置档案 ID。</param>
        /// <returns>成功移除缓存记录时返回 true，否则返回 false。</returns>
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

        /// <summary>
        /// 在线性探测哈希表中查找匹配记录所在槽位。
        /// </summary>
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

        /// <summary>
        /// 在线性探测哈希表中插入或更新一条缓存记录。
        /// </summary>
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

        /// <summary>
        /// 删除指定槽位并重新整理后续探测链。
        /// </summary>
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

        /// <summary>
        /// 清理指定槽位中的缓存数据。
        /// </summary>
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

        /// <summary>
        /// 扩容缓存表并重新插入已有记录。
        /// </summary>
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

    /// <summary>
    /// 构建绑定选择缓存使用的稳定哈希值。
    /// </summary>
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

    /// <summary>
    /// 将空字符串引用规范化为空字符串。
    /// </summary>
    private static string Normalize(string value)
    {
        return value ?? string.Empty;
    }

    /// <summary>
    /// 计算大于等于指定值的最小 2 的幂。
    /// </summary>
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
