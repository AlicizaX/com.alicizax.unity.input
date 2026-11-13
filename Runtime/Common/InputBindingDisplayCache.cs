#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    /// <summary>
    /// 绑定显示文本缓存。Rebind 与 Glyph 都需要 <c>GetBindingDisplayString</c> 且要求稳态零分配，
    /// 由 <see cref="UXInput"/> 创建一份注入两者，避免一方借用另一方的内部缓存。
    /// </summary>
    internal sealed class InputBindingDisplayCache
    {
        private readonly InputBindingVersion _version;
        private readonly Dictionary<DisplayKey, string> _strings = new Dictionary<DisplayKey, string>(16);
        private int _cachedVersion;

        public InputBindingDisplayCache(InputBindingVersion version)
        {
            _version = version;
            _cachedVersion = version.Value;
        }

        /// <summary>返回绑定的显示文本；索引无效时返回空串。</summary>
        public string Get(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
                return string.Empty;

            if (_cachedVersion != _version.Value)
            {
                _strings.Clear();
                _cachedVersion = _version.Value;
            }

            var key = new DisplayKey(action, bindingIndex);
            if (_strings.TryGetValue(key, out string cached))
                return cached;

            string value = action.GetBindingDisplayString(bindingIndex) ?? string.Empty;
            _strings.Add(key, value);
            return value;
        }

        public void Clear() => _strings.Clear();

        private readonly struct DisplayKey : IEquatable<DisplayKey>
        {
            private readonly InputAction _action;
            private readonly int _bindingIndex;

            internal DisplayKey(InputAction action, int bindingIndex)
            {
                _action = action;
                _bindingIndex = bindingIndex;
            }

            public bool Equals(DisplayKey other) =>
                ReferenceEquals(_action, other._action) && _bindingIndex == other._bindingIndex;

            public override bool Equals(object obj) => obj is DisplayKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RuntimeHelpers.GetHashCode(_action) * 397) ^ _bindingIndex;
                }
            }
        }
    }
}
#endif
