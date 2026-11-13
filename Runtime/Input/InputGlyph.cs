#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    /// <summary>
    /// 输入图标查询工具，负责根据当前输入 Profile、动作绑定或控制路径解析 UI 图标、TMP Sprite 标签和显示文本。
    /// 所有查询结果都按 Profile/绑定版本缓存，稳定运行时不再分配。
    /// </summary>
    public sealed class InputGlyph
    {
        private readonly InputWatch _watch;
        private readonly InputBindingVersion _bindingVersion;
        private readonly InputBindingDisplayCache _displayCache;

        internal InputGlyph(
            InputGlyphDatabase database,
            InputWatch watch,
            InputBindingVersion bindingVersion,
            InputBindingDisplayCache displayCache)
        {
            _database = database;
            _watch = watch;
            _bindingVersion = bindingVersion;
            _displayCache = displayCache;
        }

        private readonly Dictionary<string, string> DisplayNameCache =
            new Dictionary<string, string>(64, StringComparer.Ordinal);

        private readonly Dictionary<string, string> GlyphKeyCache =
            new Dictionary<string, string>(64, StringComparer.Ordinal);

        private readonly Dictionary<string, string> SpriteTagCache =
            new Dictionary<string, string>(64, StringComparer.Ordinal);

        private readonly Dictionary<BindingKey, int> BindingIndexCache = new Dictionary<BindingKey, int>(32);

        private InputGlyphDatabase _database;
        private int _cacheVersion = -1;

        /// <summary>
        /// 设置当前全局使用的输入图标数据库，并清空图标查询缓存。
        /// </summary>
        public void SetDatabase(InputGlyphDatabase database)
        {
            _database = database;
            ClearCaches();
        }

        internal void Shutdown()
        {
            _database = null;
            ClearCaches();
        }

        /// <summary>
        /// 获取指定输入动作在当前输入 Profile 下最匹配绑定的有效控制路径。
        /// </summary>
        public string GetBindingControlPath(InputAction action, string compositePartName = null)
        {
            if (!TryGetBinding(action, compositePartName, out _, out InputBinding binding))
            {
                return string.Empty;
            }

            return binding.effectivePath;
        }

        /// <summary>
        /// 获取指定输入动作引用在当前输入 Profile 下最匹配绑定的有效控制路径。
        /// </summary>
        public string GetBindingControlPath(InputActionReference actionReference, string compositePartName = null)
        {
            return GetBindingControlPath(actionReference != null ? actionReference.action : null, compositePartName);
        }

        /// <summary>
        /// 根据输入动作在当前输入 Profile 下的匹配绑定，尝试获取 TMP Sprite 标签。
        /// </summary>
        public bool TryGetTag(
            InputAction action,
            string compositePartName,
            out string tag,
            out string displayFallback)
        {
            return TryGetTag(GetBindingControlPath(action, compositePartName), out tag, out displayFallback);
        }

        /// <summary>
        /// 根据输入动作引用在当前输入 Profile 下的匹配绑定，尝试获取 TMP Sprite 标签。
        /// </summary>
        public bool TryGetTag(
            InputActionReference actionReference,
            string compositePartName,
            out string tag,
            out string displayFallback)
        {
            return TryGetTag(
                actionReference != null ? actionReference.action : null,
                compositePartName,
                out tag,
                out displayFallback);
        }

        /// <summary>
        /// 根据输入动作在当前输入 Profile 下的匹配绑定，尝试获取 UI Sprite 图标。
        /// </summary>
        public bool TryGetSprite(
            InputAction action,
            string compositePartName,
            out Sprite sprite)
        {
            return TryGetSprite(GetBindingControlPath(action, compositePartName), out sprite);
        }

        /// <summary>
        /// 根据输入动作引用在当前输入 Profile 下的匹配绑定，尝试获取 UI Sprite 图标。
        /// </summary>
        public bool TryGetSprite(
            InputActionReference actionReference,
            string compositePartName,
            out Sprite sprite)
        {
            return TryGetSprite(
                actionReference != null ? actionReference.action : null,
                compositePartName,
                out sprite);
        }

        /// <summary>
        /// 根据控制路径和当前输入 Profile，尝试获取 TMP Sprite 标签。
        /// 找不到明确图标时返回 false 并给出可回退显示的文本；不会用占位图冒充实测命中。
        /// </summary>
        public bool TryGetTag(
            string controlPath,
            out string tag,
            out string displayFallback)
        {
            tag = null;
            if (_database != null
                && _database.TryGetGlyph(_watch.CurrentProfileId, GetGlyphKeyFromControlPath(controlPath), out _, out string spriteName)
                && !string.IsNullOrEmpty(spriteName))
            {
                tag = GetSpriteTag(spriteName);
                displayFallback = string.Empty;
                return true;
            }

            displayFallback = GetDisplayName(controlPath);
            return false;
        }

        /// <summary>
        /// 根据控制路径和当前输入 Profile，尝试获取 UI Sprite 图标。
        /// 找不到明确图标时回退到数据库占位图。
        /// </summary>
        public bool TryGetSprite(string controlPath, out Sprite sprite)
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

            if (_database.TryGetSprite(_watch.CurrentProfileId, glyphKey, out sprite))
            {
                return true;
            }

            sprite = _database.Placeholder;
            return sprite != null;
        }

        /// <summary>
        /// 获取输入动作在当前输入 Profile 下最匹配绑定的显示文本。
        /// </summary>
        public string GetDisplayName(InputAction action, string compositePartName = null)
        {
            if (!TryGetBinding(action, compositePartName, out int bindingIndex, out InputBinding binding))
            {
                return string.Empty;
            }

            string display = _displayCache.Get(action, bindingIndex);
            if (!string.IsNullOrEmpty(display))
            {
                return display;
            }

            return GetDisplayName(binding.effectivePath);
        }

        /// <summary>
        /// 将 Unity Input System 控制路径转换为人类可读的显示文本。
        /// </summary>
        public string GetDisplayName(string controlPath)
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
        public bool TryGetBinding(
            InputAction action,
            string compositePartName,
            out InputBinding binding)
        {
            return TryGetBinding(action, compositePartName, out _, out binding);
        }

        /// <summary>
        /// 将 Unity Input System 控制路径转换为图标数据库使用的图标键。
        /// </summary>
        public string GetGlyphKeyFromControlPath(string controlPath)
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

        private bool TryGetBinding(
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

        private int FindBindingIndex(InputAction action, string compositePartName)
        {
            int version = _bindingVersion.Value;
            if (_cacheVersion != version)
            {
                BindingIndexCache.Clear();
                _cacheVersion = version;
            }

            InputWatch.InputProfile profile = _watch.CurrentInputProfile;
            var key = new BindingKey(action, compositePartName, profile);
            if (BindingIndexCache.TryGetValue(key, out int cachedIndex))
            {
                return cachedIndex;
            }

            int index = InputBindingResolver.SelectForDisplay(
                action,
                compositePartName,
                profile,
                GetBindingGroupHints(profile));
            BindingIndexCache[key] = index;
            return index;
        }

        private string[] GetBindingGroupHints(InputWatch.InputProfile profile)
        {
            if (_database != null
                && _database.TryGetBindingGroupHints(InputProfileCatalog.GetId(profile), out string[] databaseHints))
            {
                return databaseHints;
            }

            return InputProfileCatalog.TryGet(profile, out InputProfileInfo info) ? info.BindingGroupHints : null;
        }

        private void ClearCaches()
        {
            DisplayNameCache.Clear();
            GlyphKeyCache.Clear();
            SpriteTagCache.Clear();
            BindingIndexCache.Clear();
            _cacheVersion = -1;
        }

        private string BuildDisplayFallback(string controlPath)
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

        private int FindLastControlPathSegmentStart(string controlPath)
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

        private bool IsDisplayTrimChar(char value)
        {
            return value == '{'
                   || value == '}'
                   || value == '<'
                   || value == '>'
                   || value == '\''
                   || value == '"';
        }

        private string GetSpriteTag(string spriteName)
        {
            if (SpriteTagCache.TryGetValue(spriteName, out string cachedSpriteTag))
            {
                return cachedSpriteTag;
            }

            string cachedTag = string.Concat("<sprite name=\"", spriteName, "\">");
            SpriteTagCache[spriteName] = cachedTag;
            return cachedTag;
        }

        private readonly struct BindingKey : IEquatable<BindingKey>
        {
            private readonly InputAction _action;
            private readonly string _compositePartName;
            private readonly InputWatch.InputProfile _profile;
            private readonly int _hash;

            internal BindingKey(InputAction action, string compositePartName, InputWatch.InputProfile profile)
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
