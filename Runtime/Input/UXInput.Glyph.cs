#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

public static partial class UXInput
{
    /// <summary>
    /// 输入图标查询工具，负责根据当前输入 Profile、动作绑定或控制路径解析 UI 图标、TMP Sprite 标签和显示文本。
    /// 所有查询结果都按 Profile/绑定版本缓存，稳定运行时不再分配。
    /// </summary>
    public static class Glyph
    {
        private static readonly Dictionary<string, string> DisplayNameCache =
            new Dictionary<string, string>(64, StringComparer.Ordinal);

        private static readonly Dictionary<string, string> GlyphKeyCache =
            new Dictionary<string, string>(64, StringComparer.Ordinal);

        private static readonly Dictionary<string, string> SpriteTagCache =
            new Dictionary<string, string>(64, StringComparer.Ordinal);

        private static readonly Dictionary<BindingKey, int> BindingIndexCache = new Dictionary<BindingKey, int>(32);

        private static InputGlyphDatabase _database;
        private static int _cacheVersion = -1;

        /// <summary>
        /// 获取当前输入设备对应的图标 Profile ID。
        /// </summary>
        public static string CurrentProfileId => Watch.CurrentProfileId;

        /// <summary>
        /// 设置当前全局使用的输入图标数据库，并清空图标查询缓存。
        /// </summary>
        public static void SetDatabase(InputGlyphDatabase database)
        {
            _database = database;
            ClearCaches();
        }

        internal static void ResetForPlayMode()
        {
            _database = null;
            ClearCaches();
        }

        /// <summary>
        /// 获取指定输入动作在当前输入 Profile 下最匹配绑定的有效控制路径。
        /// </summary>
        public static string GetBindingControlPath(InputAction action, string compositePartName = null)
        {
            if (!TryGetBinding(action, compositePartName, out _, out InputBinding binding))
            {
                return string.Empty;
            }

            return InputStringUtility.GetEffectivePath(in binding);
        }

        /// <summary>
        /// 获取指定输入动作引用在当前输入 Profile 下最匹配绑定的有效控制路径。
        /// </summary>
        public static string GetBindingControlPath(InputActionReference actionReference, string compositePartName = null)
        {
            return GetBindingControlPath(actionReference != null ? actionReference.action : null, compositePartName);
        }

        /// <summary>
        /// 根据输入动作在当前输入 Profile 下的匹配绑定，尝试获取 TMP Sprite 标签。
        /// </summary>
        public static bool TryGetTMPTagForActionPath(
            InputAction action,
            string compositePartName,
            out string tag,
            out string displayFallback)
        {
            return TryGetTMPTagForControlPath(GetBindingControlPath(action, compositePartName), out tag, out displayFallback);
        }

        /// <summary>
        /// 根据输入动作引用在当前输入 Profile 下的匹配绑定，尝试获取 TMP Sprite 标签。
        /// </summary>
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
        public static bool TryGetUISpriteForActionPath(
            InputAction action,
            string compositePartName,
            out Sprite sprite)
        {
            return TryGetUISpriteForControlPath(GetBindingControlPath(action, compositePartName), out sprite);
        }

        /// <summary>
        /// 根据输入动作引用在当前输入 Profile 下的匹配绑定，尝试获取 UI Sprite 图标。
        /// </summary>
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
        /// 找不到明确图标时返回 false 并给出可回退显示的文本；不会用占位图冒充实测命中。
        /// </summary>
        public static bool TryGetTMPTagForControlPath(
            string controlPath,
            out string tag,
            out string displayFallback)
        {
            displayFallback = GetDisplayNameFromControlPath(controlPath);
            tag = null;

            if (_database == null)
            {
                return false;
            }

            string glyphKey = GetGlyphKeyFromControlPath(controlPath);
            if (string.IsNullOrEmpty(glyphKey))
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
        /// 找不到明确图标时回退到数据库占位图。
        /// </summary>
        public static bool TryGetUISpriteForControlPath(string controlPath, out Sprite sprite)
        {
            sprite = null;

            if (_database == null)
            {
                return false;
            }

            string glyphKey = GetGlyphKeyFromControlPath(controlPath);
            if (string.IsNullOrEmpty(glyphKey))
            {
                return false;
            }

            if (_database.TryGetSprite(CurrentProfileId, glyphKey, out sprite))
            {
                return true;
            }

            sprite = _database.Placeholder;
            return sprite != null;
        }

        /// <summary>
        /// 获取输入动作在当前输入 Profile 下最匹配绑定的显示文本。
        /// </summary>
        public static string GetDisplayNameFromInputAction(InputAction action, string compositePartName = null)
        {
            if (!TryGetBinding(action, compositePartName, out int bindingIndex, out InputBinding binding))
            {
                return string.Empty;
            }

            string display = Rebind.GetBindingDisplayString(action, bindingIndex);
            if (!string.IsNullOrEmpty(display))
            {
                return display;
            }

            return GetDisplayNameFromControlPath(InputStringUtility.GetEffectivePath(in binding));
        }

        /// <summary>
        /// 将 Unity Input System 控制路径转换为人类可读的显示文本。
        /// </summary>
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
        public static bool TryGetBindingControl(
            InputAction action,
            string compositePartName,
            out InputBinding binding)
        {
            return TryGetBinding(action, compositePartName, out _, out binding);
        }

        /// <summary>
        /// 将 Unity Input System 控制路径转换为图标数据库使用的图标键。
        /// </summary>
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

        private static bool TryGetBinding(
            InputAction action,
            string compositePartName,
            out int bindingIndex,
            out InputBinding binding)
        {
            bindingIndex = -1;
            binding = default;
            if (action == null)
            {
                return false;
            }

            bindingIndex = FindBindingIndex(action, compositePartName);
            if (bindingIndex < 0)
            {
                return false;
            }

            binding = action.bindings[bindingIndex];
            return true;
        }

        private static int FindBindingIndex(InputAction action, string compositePartName)
        {
            int version = Rebind.BindingVersion;
            if (_cacheVersion != version)
            {
                BindingIndexCache.Clear();
                _cacheVersion = version;
            }

            var key = new BindingKey(action, compositePartName, Watch.CurrentInputProfile);
            if (BindingIndexCache.TryGetValue(key, out int cachedIndex))
            {
                return cachedIndex;
            }

            int index = InputBindingResolver.SelectForDisplay(
                action,
                compositePartName,
                Watch.CurrentInputProfile,
                GetBindingGroupHints(Watch.CurrentInputProfile));
            BindingIndexCache[key] = index;
            return index;
        }

        private static string[] GetBindingGroupHints(Watch.InputProfile profile)
        {
            if (_database != null
                && _database.TryGetBindingGroupHints(Watch.CurrentProfileId, out string[] databaseHints))
            {
                return databaseHints;
            }

            return InputProfileCatalog.TryGet(profile, out InputProfileInfo info) ? info.BindingGroupHints : null;
        }

        private static void ClearCaches()
        {
            DisplayNameCache.Clear();
            GlyphKeyCache.Clear();
            SpriteTagCache.Clear();
            BindingIndexCache.Clear();
            _cacheVersion = -1;
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

            return controlPath.Substring(start, end - start + 1);
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
            string resolvedSpriteName = !string.IsNullOrEmpty(spriteName)
                ? spriteName
                : (sprite != null ? sprite.name : null);
            if (string.IsNullOrEmpty(resolvedSpriteName))
            {
                return null;
            }

            if (SpriteTagCache.TryGetValue(resolvedSpriteName, out string cachedSpriteTag))
            {
                return cachedSpriteTag;
            }

            string cachedTag = string.Concat("<sprite name=\"", resolvedSpriteName, "\">");
            SpriteTagCache[resolvedSpriteName] = cachedTag;
            return cachedTag;
        }

        private readonly struct BindingKey : IEquatable<BindingKey>
        {
            private readonly InputAction _action;
            private readonly string _compositePartName;
            private readonly Watch.InputProfile _profile;
            private readonly int _hash;

            internal BindingKey(InputAction action, string compositePartName, Watch.InputProfile profile)
            {
                _action = action;
                _compositePartName = compositePartName ?? string.Empty;
                _profile = profile;
                unchecked
                {
                    int partHash = _compositePartName.Length == 0
                        ? 0
                        : StringComparer.OrdinalIgnoreCase.GetHashCode(_compositePartName);
                    _hash = (RuntimeHelpers.GetHashCode(action) * 397) ^ partHash ^ ((int)profile << 16);
                }
            }

            public bool Equals(BindingKey other)
            {
                return ReferenceEquals(_action, other._action)
                       && _profile == other._profile
                       && string.Equals(_compositePartName, other._compositePartName, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is BindingKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _hash;
            }
        }
    }
}
#endif
