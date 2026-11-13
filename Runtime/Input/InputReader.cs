#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace AlicizaX.Input
{
    /// <summary>
    /// 唯一持有 <see cref="InputActionAsset"/> 的模块：负责动作解析（唯一规则：完整 "Map/Action" 路径）、
    /// 启停动作与读取当前状态。边沿与开关状态由调用方持有的 <see cref="ButtonState"/> 维护。
    /// </summary>
    public sealed class InputReader
    {
        private readonly Dictionary<string, InputAction> _actionLookup =
            new Dictionary<string, InputAction>(64, StringComparer.Ordinal);

        // 字符串重载的句柄缓存；未找到的部件也缓存为 null，避免每帧重复查找。
        private readonly Dictionary<CompositePartKey, InputCompositePart> _compositeParts =
            new Dictionary<CompositePartKey, InputCompositePart>(32);

        private readonly InputBindingVersion _bindingVersion;
        private InputActionAsset _actions;

        internal InputReader(InputActionAsset actions, InputBindingVersion bindingVersion)
        {
            _actions = actions;
            _bindingVersion = bindingVersion;
        }

        internal InputActionAsset Actions => _actions;

        internal void Initialize()
        {
            if (_actions == null) return;
            foreach (InputActionMap map in _actions.actionMaps)
                foreach (InputAction action in map.actions)
                    _actionLookup.Add(map.name + "/" + action.name, action);
            _actions.Enable();
        }

        internal void Shutdown()
        {
            _actions?.Disable();
            _actions = null;
            _actionLookup.Clear();
            _compositeParts.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            OnRead = null;
            OnContinuousRead = null;
#endif
        }

        /// <summary>按完整 "Map/Action" 路径解析动作。</summary>
        public bool TryResolveAction(string actionPath, out InputAction action)
        {
            if (!string.IsNullOrWhiteSpace(actionPath))
                return _actionLookup.TryGetValue(actionPath, out action);
            action = null;
            return false;
        }

        /// <summary>按完整 "Map/Action" 路径解析动作；未找到时返回 null，不产生日志。</summary>
        public InputAction ResolveAction(string actionPath)
        {
            return TryResolveAction(actionPath, out InputAction action) ? action : null;
        }

        public InputActionMap FindActionMap(string mapName, bool throwIfNotFound = false)
        {
            if (_actions != null)
                return _actions.FindActionMap(mapName, throwIfNotFound);

            if (throwIfNotFound)
                throw new InvalidOperationException("InputActionAsset is not available.");

            return null;
        }

        /// <summary>
        /// 解析组合绑定部件句柄。高频读取应缓存返回值并调用 <see cref="InputCompositePart.IsPressed"/>，
        /// 读取时不再查字典、不再做字符串哈希。找不到该部件时返回 null。
        /// </summary>
        public InputCompositePart ResolveCompositePart(InputAction action, string compositePartName)
        {
            if (action == null || string.IsNullOrWhiteSpace(compositePartName))
                return null;

            var key = new CompositePartKey(action, compositePartName);
            if (_compositeParts.TryGetValue(key, out InputCompositePart part))
                return part;

            part = InputBindingResolver.FindCompositePartBindingIndex(action, compositePartName) >= 0
                ? new InputCompositePart(action, compositePartName, _bindingVersion)
                : null;
            _compositeParts.Add(key, part);
            return part;
        }

        public T ReadValue<T>(InputAction action) where T : struct
        {
            return action != null ? action.ReadValue<T>() : default;
        }

        public T ReadValue<T>(string actionPath) where T : struct
        {
            return ReadValue<T>(ResolveActionOrAssert(actionPath));
        }

        /// <summary>动作处于按下状态时读取当前值。</summary>
        public bool TryReadValue<T>(InputAction action, out T value) where T : struct
        {
            if (action != null && action.IsPressed())
            {
                value = action.ReadValue<T>();
                RaiseContinuousReadTrace(ReadTraceKind.Value, action, null, true);
                return true;
            }

            value = default;
            return false;
        }

        public bool TryReadValue<T>(string actionPath, out T value) where T : struct
        {
            return TryReadValue(ResolveActionOrAssert(actionPath), out value);
        }

        /// <summary>动作当前是否处于按下（驱动）状态。</summary>
        public bool IsPressed(InputAction action)
        {
            bool isPressed = action != null && action.IsPressed();
            if (isPressed)
                RaiseContinuousReadTrace(ReadTraceKind.Pressed, action, null, true);
            return isPressed;
        }

        public bool IsPressed(string actionPath)
        {
            return IsPressed(ResolveActionOrAssert(actionPath));
        }

        /// <summary>组合绑定部件当前是否按下。</summary>
        public bool IsPressed(InputCompositePart part)
        {
            bool isPressed = part != null && part.IsPressed();
            if (isPressed)
                RaiseContinuousReadTrace(ReadTraceKind.CompositePartButton, part.Action, part.PartName, true);
            return isPressed;
        }

        /// <summary>便捷重载：每次调用做一次句柄字典查找。高频路径请缓存 <see cref="ResolveCompositePart"/> 的结果。</summary>
        public bool IsCompositePartPressed(InputAction action, string compositePartName)
        {
            return IsPressed(ResolveCompositePart(action, compositePartName));
        }

        public bool IsCompositePartPressed(string actionPath, string compositePartName)
        {
            return IsCompositePartPressed(ResolveActionOrAssert(actionPath), compositePartName);
        }

        private InputAction ResolveActionOrAssert(string actionPath)
        {
            InputAction action = ResolveAction(actionPath);
            if (action == null)
                Debug.Assert(false, "Unresolved input action '" + actionPath + "'. Use the full 'Map/Action' path.");
            return action;
        }

        #region Diagnostics

        /// <summary>诊断读取类型。</summary>
        public enum ReadTraceKind : byte
        {
            Value,
            Button,
            Pressed,
            Once,
            Toggle,
            CompositePartButton,
            CompositePartOnce,
            CompositePartToggle
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>诊断读取记录。仅编辑器与开发版存在。</summary>
        public readonly struct ReadTrace
        {
            public readonly ReadTraceKind Kind;
            public readonly InputAction Action;
            public readonly string CompositePartName;
            public readonly bool BoolValue;

            public ReadTrace(ReadTraceKind kind, InputAction action, string compositePartName, bool boolValue)
            {
                Kind = kind;
                Action = action;
                CompositePartName = compositePartName;
                BoolValue = boolValue;
            }
        }

        /// <summary>一次触发、切换读取发生时触发。仅编辑器与开发版存在。</summary>
        public event Action<ReadTrace> OnRead;

        /// <summary>连续读取读到有效按下状态时触发，可能高频。仅编辑器与开发版存在。</summary>
        public event Action<ReadTrace> OnContinuousRead;
#endif

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void RaiseReadTrace(ReadTraceKind kind, InputAction action, string compositePartName, bool boolValue)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            OnRead?.Invoke(new ReadTrace(kind, action, compositePartName, boolValue));
#endif
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private void RaiseContinuousReadTrace(ReadTraceKind kind, InputAction action, string compositePartName, bool boolValue)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            OnContinuousRead?.Invoke(new ReadTrace(kind, action, compositePartName, boolValue));
#endif
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        private static void TraceButtonState(ReadTraceKind kind, InputAction action, string compositePartName, bool boolValue)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            InputReader reader = UXInput.Reader;
            if (reader != null) reader.RaiseReadTrace(kind, action, compositePartName, boolValue);
#endif
        }

        #endregion

        /// <summary>
        /// 调用方持有的按钮边沿 / 开关状态。声明为字段使用，不要作为属性（属性返回副本会丢失状态）。
        /// 推荐重载：<see cref="InputAction"/> 或 <see cref="InputCompositePart"/>，二者不依赖全局状态。
        /// </summary>
        public struct ButtonState
        {
            private bool _wasPressed;
            private bool _toggled;

            /// <summary>本次按下周期的首次按下。</summary>
            public bool Once(InputAction action)
            {
                bool triggered = Advance(action != null && action.IsPressed());
                if (triggered) TraceButtonState(ReadTraceKind.Once, action, null, true);
                return triggered;
            }

            /// <summary>组合绑定部件的本次按下周期首次按下。</summary>
            public bool Once(InputCompositePart part)
            {
                bool triggered = Advance(part != null && part.IsPressed());
                if (triggered) TraceButtonState(ReadTraceKind.CompositePartOnce, part.Action, part.PartName, true);
                return triggered;
            }

            /// <summary>便捷重载：部件名为空时等价于 <see cref="Once(InputAction)"/>；否则经 <see cref="UXInput.Reader"/> 解析句柄。</summary>
            public bool Once(InputAction action, string compositePartName)
            {
                return string.IsNullOrWhiteSpace(compositePartName)
                    ? Once(action)
                    : Once(UXInput.Reader?.ResolveCompositePart(action, compositePartName));
            }

            /// <summary>每次首次按下时切换并返回缓存状态。</summary>
            public bool Toggle(InputAction action)
            {
                if (Advance(action != null && action.IsPressed()))
                {
                    _toggled = !_toggled;
                    TraceButtonState(ReadTraceKind.Toggle, action, null, _toggled);
                }

                return _toggled;
            }

            /// <summary>组合绑定部件每次首次按下时切换并返回缓存状态。</summary>
            public bool Toggle(InputCompositePart part)
            {
                if (Advance(part != null && part.IsPressed()))
                {
                    _toggled = !_toggled;
                    TraceButtonState(ReadTraceKind.CompositePartToggle, part.Action, part.PartName, _toggled);
                }

                return _toggled;
            }

            /// <summary>便捷重载：部件名为空时等价于 <see cref="Toggle(InputAction)"/>；否则经 <see cref="UXInput.Reader"/> 解析句柄。</summary>
            public bool Toggle(InputAction action, string compositePartName)
            {
                return string.IsNullOrWhiteSpace(compositePartName)
                    ? Toggle(action)
                    : Toggle(UXInput.Reader?.ResolveCompositePart(action, compositePartName));
            }

            /// <summary>清零边沿与开关状态。</summary>
            public void Reset()
            {
                _wasPressed = false;
                _toggled = false;
            }

            /// <summary>直接设置开关状态（不改变边沿）。</summary>
            public void SetToggled(bool value)
            {
                _toggled = value;
            }

            private bool Advance(bool pressed)
            {
                bool triggered = pressed && !_wasPressed;
                _wasPressed = pressed;
                return triggered;
            }
        }

        private readonly struct CompositePartKey : IEquatable<CompositePartKey>
        {
            private readonly InputAction _action;
            private readonly string _partName;
            private readonly int _hash;

            internal CompositePartKey(InputAction action, string partName)
            {
                _action = action;
                _partName = partName;
                unchecked
                {
                    _hash = (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(action) * 397)
                            ^ StringComparer.OrdinalIgnoreCase.GetHashCode(partName);
                }
            }

            public bool Equals(CompositePartKey other)
            {
                return ReferenceEquals(_action, other._action)
                       && string.Equals(_partName, other._partName, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj) => obj is CompositePartKey other && Equals(other);

            public override int GetHashCode() => _hash;
        }
    }

    /// <summary>
    /// 组合绑定部件句柄（例如 Move 的 "Up"）。缓存该部件当前解析到的控件，
    /// 绑定版本变化（覆盖、设备增删、绑定增删）时惰性重建；稳态读取只遍历 1~2 个控件。
    /// 由 <see cref="InputReader.ResolveCompositePart"/> 创建。
    /// </summary>
    public sealed class InputCompositePart
    {
        private readonly InputBindingVersion _bindingVersion;
        private InputControl[] _controls = Array.Empty<InputControl>();
        private int _controlCount;
        private int _resolvedVersion;
        private bool _resolved;

        internal InputCompositePart(InputAction action, string partName, InputBindingVersion bindingVersion)
        {
            Action = action;
            PartName = partName;
            _bindingVersion = bindingVersion;
        }

        public InputAction Action { get; }

        public string PartName { get; }

        public bool IsPressed()
        {
            if (!Action.enabled) return false;
            if (!_resolved || _resolvedVersion != _bindingVersion.ControlsValue) ResolveControls();

            for (int i = 0; i < _controlCount; i++)
            {
                if (_controls[i].IsPressed())
                    return true;
            }

            return false;
        }

        private void ResolveControls()
        {
            _controlCount = 0;
            // 按名字重新定位：绑定被增删时索引会变，名字不会。
            int bindingIndex = InputBindingResolver.FindCompositePartBindingIndex(Action, PartName);
            string path = bindingIndex >= 0 ? Action.bindings[bindingIndex].effectivePath : null;
            if (!string.IsNullOrWhiteSpace(path))
            {
                // 同布局可能有多个设备实例，全部收集，避免固定首个控件导致漏按。
                var controls = Action.controls;
                for (int i = 0; i < controls.Count; i++)
                {
                    InputControl control = controls[i];
                    if (!InputControlPath.Matches(path, control)) continue;
                    if (_controlCount == _controls.Length)
                        Array.Resize(ref _controls, Math.Max(2, _controlCount * 2));
                    _controls[_controlCount++] = control;
                }
            }

            // 读取 controls 可能触发解析并推进版本号，所以在读完之后再记录。
            _resolvedVersion = _bindingVersion.ControlsValue;
            _resolved = true;
        }
    }
}
#endif
