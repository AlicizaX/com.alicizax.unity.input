#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public static partial class UXInput
{
    /// <summary>
    /// 输入图标查询工具，负责根据当前输入设备 Profile、动作绑定或控制路径解析 UI 图标、TMP Sprite 标签和显示文本。
    /// </summary>
    public static class Glyph
    {
        private const int InitialCacheCapacity = 64;

        private static readonly Dictionary<string, string> DisplayNameCache =
            new Dictionary<string, string>(InitialCacheCapacity, StringComparer.Ordinal);

        private static readonly Dictionary<string, string> GlyphKeyCache =
            new Dictionary<string, string>(InitialCacheCapacity, StringComparer.Ordinal);

        private static readonly Dictionary<string, string> SpriteTagCache =
            new Dictionary<string, string>(InitialCacheCapacity, StringComparer.Ordinal);

        private static InputGlyphDatabase _database;

        /// <summary>
        /// 获取当前输入设备对应的图标 Profile ID。
        /// </summary>
        public static string CurrentProfileId
        {
            get
            {
                return Watch.CurrentInputProfile.ToString();
            }
        }

        /// <summary>
        /// 设置当前全局使用的输入图标数据库，并清空图标查询缓存。
        /// </summary>
        /// <param name="database">要设置为当前全局数据源的输入图标数据库；传入 null 时清空当前数据源。</param>
        public static void SetDatabase(InputGlyphDatabase database)
        {
            _database = database;
            DisplayNameCache.Clear();
            GlyphKeyCache.Clear();
            SpriteTagCache.Clear();
        }

        /// <summary>
        /// 获取指定输入动作在当前输入 Profile 下最匹配绑定的有效控制路径。
        /// </summary>
        /// <param name="action">要查询绑定路径的输入动作。</param>
        /// <param name="compositePartName">可选的组合绑定部分名称；为空时查询普通绑定。</param>
        /// <returns>匹配绑定的有效控制路径；未找到匹配绑定时返回空字符串。</returns>
        public static string GetBindingControlPath(InputAction action, string compositePartName = null)
        {
            return TryGetBindingControl(action, compositePartName, out InputBinding binding)
                ? GetEffectivePath(binding)
                : string.Empty;
        }

        /// <summary>
        /// 获取指定输入动作引用在当前输入 Profile 下最匹配绑定的有效控制路径。
        /// </summary>
        /// <param name="actionReference">要查询绑定路径的输入动作引用。</param>
        /// <param name="compositePartName">可选的组合绑定部分名称；为空时查询普通绑定。</param>
        /// <returns>匹配绑定的有效控制路径；引用为空或未找到匹配绑定时返回空字符串。</returns>
        public static string GetBindingControlPath(InputActionReference actionReference, string compositePartName = null)
        {
            return GetBindingControlPath(actionReference != null ? actionReference.action : null, compositePartName);
        }

        /// <summary>
        /// 根据输入动作在当前输入 Profile 下的匹配绑定，尝试获取 TMP Sprite 标签。
        /// </summary>
        /// <param name="action">要查询图标的输入动作。</param>
        /// <param name="compositePartName">可选的组合绑定部分名称；为空时查询普通绑定。</param>
        /// <param name="tag">输出 TMP Sprite 标签，例如 &lt;sprite name="A"&gt;；查询失败时为 null。</param>
        /// <param name="displayFallback">输出绑定的文本显示名，用于图标缺失时回退显示。</param>
        /// <returns>成功解析 TMP Sprite 标签时返回 true，否则返回 false。</returns>
        public static bool TryGetTMPTagForActionPath(
            InputAction action,
            string compositePartName,
            out string tag,
            out string displayFallback)
        {
            string controlPath = GetBindingControlPath(action, compositePartName);
            return TryGetTMPTagForControlPath(controlPath, out tag, out displayFallback);
        }

        /// <summary>
        /// 根据输入动作引用在当前输入 Profile 下的匹配绑定，尝试获取 TMP Sprite 标签。
        /// </summary>
        /// <param name="actionReference">要查询图标的输入动作引用。</param>
        /// <param name="compositePartName">可选的组合绑定部分名称；为空时查询普通绑定。</param>
        /// <param name="tag">输出 TMP Sprite 标签，例如 &lt;sprite name="A"&gt;；查询失败时为 null。</param>
        /// <param name="displayFallback">输出绑定的文本显示名，用于图标缺失时回退显示。</param>
        /// <returns>成功解析 TMP Sprite 标签时返回 true，否则返回 false。</returns>
        public static bool TryGetTMPTagForActionPath(
            InputActionReference actionReference,
            string compositePartName,
            out string tag,
            out string displayFallback)
        {
            return TryGetTMPTagForActionPath(
                actionReference != null ? actionReference.action : null,
                compositePartName,
                out tag,
                out displayFallback);
        }

        /// <summary>
        /// 根据输入动作在当前输入 Profile 下的匹配绑定，尝试获取 UI Sprite 图标。
        /// </summary>
        /// <param name="action">要查询图标的输入动作。</param>
        /// <param name="compositePartName">可选的组合绑定部分名称；为空时查询普通绑定。</param>
        /// <param name="sprite">输出匹配的 UI Sprite 图标；查询失败时为 null。</param>
        /// <returns>成功解析 UI Sprite 图标时返回 true，否则返回 false。</returns>
        public static bool TryGetUISpriteForActionPath(
            InputAction action,
            string compositePartName,
            out Sprite sprite)
        {
            string controlPath = GetBindingControlPath(action, compositePartName);
            return TryGetUISpriteForControlPath(controlPath, out sprite);
        }

        /// <summary>
        /// 根据输入动作引用在当前输入 Profile 下的匹配绑定，尝试获取 UI Sprite 图标。
        /// </summary>
        /// <param name="actionReference">要查询图标的输入动作引用。</param>
        /// <param name="compositePartName">可选的组合绑定部分名称；为空时查询普通绑定。</param>
        /// <param name="sprite">输出匹配的 UI Sprite 图标；查询失败时为 null。</param>
        /// <returns>成功解析 UI Sprite 图标时返回 true，否则返回 false。</returns>
        public static bool TryGetUISpriteForActionPath(
            InputActionReference actionReference,
            string compositePartName,
            out Sprite sprite)
        {
            return TryGetUISpriteForActionPath(
                actionReference != null ? actionReference.action : null,
                compositePartName,
                out sprite);
        }

        /// <summary>
        /// 根据控制路径和当前输入 Profile，尝试获取 TMP Sprite 标签。
        /// </summary>
        /// <param name="controlPath">Unity Input System 控制路径，例如 &lt;Gamepad&gt;/buttonSouth。</param>
        /// <param name="tag">输出 TMP Sprite 标签，例如 &lt;sprite name="A"&gt;；查询失败时为 null。</param>
        /// <param name="displayFallback">输出控制路径的人类可读显示名，用于图标缺失时回退显示。</param>
        /// <returns>成功解析 TMP Sprite 标签时返回 true，否则返回 false。</returns>
        public static bool TryGetTMPTagForControlPath(
            string controlPath,
            out string tag,
            out string displayFallback)
        {
            displayFallback = GetDisplayNameFromControlPath(controlPath);
            tag = null;

            string glyphKey = GetGlyphKeyFromControlPath(controlPath);
            if (string.IsNullOrEmpty(glyphKey))
            {
                return false;
            }

            if (_database == null)
            {
                return false;
            }

            if (!_database.TryGetGlyph(CurrentProfileId, glyphKey, out Sprite sprite, out string spriteName))
            {
                return false;
            }

            tag = GetSpriteTag(sprite, spriteName);
            return tag != null;
        }

        /// <summary>
        /// 根据控制路径和当前输入 Profile，尝试获取 UI Sprite 图标。
        /// </summary>
        /// <param name="controlPath">Unity Input System 控制路径，例如 &lt;Gamepad&gt;/buttonSouth。</param>
        /// <param name="sprite">输出匹配的 UI Sprite 图标；查询失败时为 null。</param>
        /// <returns>成功解析 UI Sprite 图标时返回 true，否则返回 false。</returns>
        public static bool TryGetUISpriteForControlPath(
            string controlPath,
            out Sprite sprite)
        {
            sprite = null;

            string glyphKey = GetGlyphKeyFromControlPath(controlPath);
            if (string.IsNullOrEmpty(glyphKey))
            {
                return false;
            }

            return _database != null && _database.TryGetSprite(CurrentProfileId, glyphKey, out sprite);
        }

        /// <summary>
        /// 获取输入动作在当前输入 Profile 下最匹配绑定的显示文本。
        /// </summary>
        /// <param name="action">要查询显示文本的输入动作。</param>
        /// <param name="compositePartName">可选的组合绑定部分名称；为空时查询普通绑定。</param>
        /// <returns>匹配绑定的人类可读显示文本；未找到匹配绑定时返回空字符串。</returns>
        public static string GetDisplayNameFromInputAction(InputAction action, string compositePartName = null)
        {
            if (!TryGetBindingControl(action, compositePartName, out InputBinding binding))
            {
                return string.Empty;
            }

            string display = binding.ToDisplayString();
            return string.IsNullOrEmpty(display) ? GetDisplayNameFromControlPath(GetEffectivePath(binding)) : display;
        }

        /// <summary>
        /// 将 Unity Input System 控制路径转换为人类可读的显示文本。
        /// </summary>
        /// <param name="controlPath">Unity Input System 控制路径，例如 &lt;Keyboard&gt;/space。</param>
        /// <returns>控制路径的人类可读显示文本；控制路径为空时返回空字符串。</returns>
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

            string humanReadable = InputControlPath.ToHumanReadableString(
                controlPath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            if (!string.IsNullOrWhiteSpace(humanReadable))
            {
                DisplayNameCache[controlPath] = humanReadable;
                return humanReadable;
            }

            string fallback = BuildDisplayFallback(controlPath);
            DisplayNameCache[controlPath] = fallback;
            return fallback;
        }

        /// <summary>
        /// 在输入动作上查找当前输入 Profile 下最匹配的绑定。
        /// </summary>
        /// <param name="action">要查询绑定的输入动作。</param>
        /// <param name="compositePartName">可选的组合绑定部分名称；为空时查询普通绑定。</param>
        /// <param name="binding">输出匹配到的输入绑定；未找到时为默认值。</param>
        /// <returns>找到匹配绑定时返回 true，否则返回 false。</returns>
        public static bool TryGetBindingControl(
            InputAction action,
            string compositePartName,
            out InputBinding binding)
        {
            binding = default;
            if (action == null)
            {
                return false;
            }

            string profileId = CurrentProfileId;
            string[] bindingGroupHints = null;
            if (_database != null && _database.TryGetBindingGroupHints(profileId, out string[] hints))
            {
                bindingGroupHints = hints;
            }

            int bindingIndex = FindBestBindingIndex(action, compositePartName, profileId, bindingGroupHints);
            if (bindingIndex < 0)
            {
                return false;
            }

            binding = action.bindings[bindingIndex];
            return true;
        }

        /// <summary>
        /// 将 Unity Input System 控制路径转换为图标数据库使用的图标键。
        /// </summary>
        /// <param name="controlPath">Unity Input System 控制路径，例如 &lt;Gamepad&gt;/buttonSouth。</param>
        /// <returns>图标数据库使用的规范化图标键；控制路径为空或格式无效时返回空字符串。</returns>
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

            string glyphKey = InputGlyphPathUtility.GetGlyphKeyFromControlPath(controlPath);
            GlyphKeyCache[controlPath] = glyphKey;
            return glyphKey;
        }

        private static int FindBestBindingIndex(
            InputAction action,
            string compositePartName,
            string profileId,
            string[] bindingGroupHints)
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
                    if (!candidate.isPartOfComposite ||
                        !string.Equals(candidate.name, compositePartName, StringComparison.OrdinalIgnoreCase))
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

                int score = ScoreBinding(candidate, path, profileId, bindingGroupHints);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestBindingIndex = i;
                }
            }

            return bestScore > int.MinValue ? bestBindingIndex : -1;
        }

        private static int ScoreBinding(
            InputBinding binding,
            string path,
            string profileId,
            string[] bindingGroupHints)
        {
            int score = 0;
            if (MatchesBindingGroups(binding.groups, bindingGroupHints))
            {
                score += 100;
            }
            else if (!string.IsNullOrWhiteSpace(binding.groups))
            {
                score -= 20;
            }

            if (MatchesControlPath(path, profileId))
            {
                score += 60;
            }

            if (!binding.isPartOfComposite)
            {
                score += 5;
            }

            return score;
        }

        private static bool MatchesBindingGroups(string groups, string[] hints)
        {
            if (string.IsNullOrWhiteSpace(groups) || hints == null)
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

                if (tokenLength > 0 && ContainsAny(groups, tokenStart, tokenLength, hints))
                {
                    return true;
                }

                tokenStart = i + 1;
            }

            return false;
        }

        private static bool MatchesControlPath(string path, string profileId)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (string.Equals(profileId, Watch.InputProfile.KeyboardMouse.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return path.StartsWith("<Keyboard>", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("<Mouse>", StringComparison.OrdinalIgnoreCase);
            }

            return path.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("<Joystick>", StringComparison.OrdinalIgnoreCase)
                   || ContainsIgnoreCase(path, profileId);
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
                       && ToUpperInvariantFast(source[i + valueIndex]) == ToUpperInvariantFast(value[valueIndex]))
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

        private static bool ContainsIgnoreCase(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value) || value.Length > source.Length)
            {
                return false;
            }

            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static char ToUpperInvariantFast(char value)
        {
            return value >= 'a' && value <= 'z' ? (char)(value - 32) : char.ToUpperInvariant(value);
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
            SpriteTagCache[resolvedSpriteName] = cachedTag;
            return cachedTag;
        }

        private static string GetEffectivePath(InputBinding binding)
        {
            return string.IsNullOrWhiteSpace(binding.effectivePath) ? binding.path : binding.effectivePath;
        }

    }
}
#endif
