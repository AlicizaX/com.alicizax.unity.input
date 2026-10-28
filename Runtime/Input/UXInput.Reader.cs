#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.InputSystem;

public static partial class UXInput
{
    /// <summary>
    /// 输入动作读取工具。所有读取方法只读取 Input System 状态，不保存任何以对象/字符串为键的全局状态；
    /// 边沿与开关状态由调用方持有的 <see cref="Reader.ButtonState"/> 维护。
    /// </summary>
    public static class Reader
    {
        private static readonly Dictionary<CompositePartKey, int> CompositePartBindings =
            new Dictionary<CompositePartKey, int>(32);

        private static int _bindingVersion = -1;

        /// <summary>
        /// Reader 诊断读取类型。
        /// </summary>
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

        /// <summary>
        /// Reader 诊断读取记录。
        /// </summary>
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

        /// <summary>
        /// 在一次触发、切换读取发生时触发。
        /// </summary>
        public static event Action<ReadTrace> OnRead;

        /// <summary>
        /// 在连续读取 API 读到有效按下状态时触发，可能高频触发。
        /// </summary>
        public static event Action<ReadTrace> OnContinuousRead;

        /// <summary>
        /// 根据动作名称解析输入动作；未找到时返回 null，不产生日志。
        /// </summary>
        public static InputAction ResolveAction(string actionName)
        {
            return InputActionProvider.TryResolveAction(actionName, out InputAction action) ? action : null;
        }

        /// <summary>
        /// 读取指定输入动作的当前值。
        /// </summary>
        public static T ReadValue<T>(InputAction action) where T : struct
        {
            return action != null ? action.ReadValue<T>() : default;
        }

        /// <summary>
        /// 根据动作名称读取输入动作的当前值。
        /// </summary>
        public static T ReadValue<T>(string actionName) where T : struct
        {
            return ReadValue<T>(ResolveAction(actionName));
        }

        /// <summary>
        /// 当输入动作处于按下状态时尝试读取当前值。
        /// </summary>
        public static bool TryReadValue<T>(InputAction action, out T value) where T : struct
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

        /// <summary>
        /// 根据动作名称，在输入动作处于按下状态时尝试读取当前值。
        /// </summary>
        public static bool TryReadValue<T>(string actionName, out T value) where T : struct
        {
            return TryReadValue(ResolveAction(actionName), out value);
        }

        /// <summary>
        /// 判断按钮类型输入动作当前是否按下。
        /// </summary>
        public static bool ReadButton(InputAction action)
        {
            bool isPressed = action != null && action.type == InputActionType.Button && action.IsPressed();
            if (isPressed)
            {
                RaiseContinuousReadTrace(ReadTraceKind.Button, action, null, true);
            }

            return isPressed;
        }

        /// <summary>
        /// 根据动作名称判断按钮类型输入动作当前是否按下。
        /// </summary>
        public static bool ReadButton(string actionName)
        {
            return ReadButton(ResolveAction(actionName));
        }

        /// <summary>
        /// 判断输入动作当前是否处于按下状态。
        /// </summary>
        public static bool ReadPressed(InputAction action)
        {
            bool isPressed = action != null && action.IsPressed();
            if (isPressed)
            {
                RaiseContinuousReadTrace(ReadTraceKind.Pressed, action, null, true);
            }

            return isPressed;
        }

        /// <summary>
        /// 根据动作名称判断输入动作当前是否处于按下状态。
        /// </summary>
        public static bool ReadPressed(string actionName)
        {
            return ReadPressed(ResolveAction(actionName));
        }

        /// <summary>
        /// 读取组合绑定中指定部分的按钮按下状态。
        /// </summary>
        public static bool ReadCompositePartButton(InputAction action, string compositePartName)
        {
            bool isPressed = IsCompositePartPressed(action, compositePartName);
            if (isPressed)
            {
                RaiseContinuousReadTrace(ReadTraceKind.CompositePartButton, action, compositePartName, true);
            }

            return isPressed;
        }

        /// <summary>
        /// 根据动作名称读取组合绑定中指定部分的按钮按下状态。
        /// </summary>
        public static bool ReadCompositePartButton(string actionName, string compositePartName)
        {
            return ReadCompositePartButton(ResolveAction(actionName), compositePartName);
        }

        internal static void ResetForPlayMode()
        {
            CompositePartBindings.Clear();
            _bindingVersion = -1;
            OnRead = null;
            OnContinuousRead = null;
        }

        private static bool IsCompositePartPressed(InputAction action, string compositePartName)
        {
            if (action == null || !action.enabled || string.IsNullOrWhiteSpace(compositePartName))
            {
                return false;
            }

            int bindingIndex = GetCompositePartBindingIndex(action, compositePartName);
            if (bindingIndex < 0)
            {
                return false;
            }

            InputBinding binding = action.bindings[bindingIndex];
            string path = InputStringUtility.GetEffectivePath(in binding);
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // 同布局可能有多个设备实例，逐控制检查，避免固定首个控制导致漏按。
            var controls = action.controls;
            for (int i = 0; i < controls.Count; i++)
            {
                InputControl control = controls[i];
                if (InputControlPath.Matches(path, control) && control.IsPressed())
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetCompositePartBindingIndex(InputAction action, string compositePartName)
        {
            int version = Rebind.BindingVersion;
            if (_bindingVersion != version)
            {
                CompositePartBindings.Clear();
                _bindingVersion = version;
            }

            var key = new CompositePartKey(action, compositePartName);
            if (CompositePartBindings.TryGetValue(key, out int bindingIndex))
            {
                return bindingIndex;
            }

            bindingIndex = InputBindingResolver.FindCompositePartBindingIndex(action, compositePartName);
            CompositePartBindings[key] = bindingIndex;
            return bindingIndex;
        }

        private static bool IsPressed(InputAction action, string compositePartName)
        {
            if (action == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(compositePartName))
            {
                return action.IsPressed();
            }

            return IsCompositePartPressed(action, compositePartName);
        }

        private static void RaiseReadTrace(
            ReadTraceKind kind,
            InputAction action,
            string compositePartName,
            bool boolValue)
        {
            Action<ReadTrace> handler = OnRead;
            if (handler == null)
            {
                return;
            }

            handler(new ReadTrace(kind, action, compositePartName, boolValue));
        }

        private static void RaiseContinuousReadTrace(
            ReadTraceKind kind,
            InputAction action,
            string compositePartName,
            bool boolValue)
        {
            Action<ReadTrace> handler = OnContinuousRead;
            if (handler == null)
            {
                return;
            }

            handler(new ReadTrace(kind, action, compositePartName, boolValue));
        }

        /// <summary>
        /// 调用方持有的按钮边沿/开关状态。声明为字段使用，不要作为属性（属性返回副本会丢失状态）。
        /// </summary>
        public struct ButtonState
        {
            private bool _wasPressed;
            private bool _toggled;

            /// <summary>
            /// 本次按下周期的首次按下。
            /// </summary>
            public bool Once(InputAction action)
            {
                return Once(action, null);
            }

            /// <summary>
            /// 组合绑定指定部分的本次按下周期首次按下。
            /// </summary>
            public bool Once(InputAction action, string compositePartName)
            {
                bool pressed = IsPressed(action, compositePartName);
                bool triggered = pressed && !_wasPressed;
                _wasPressed = pressed;
                if (triggered)
                {
                    RaiseReadTrace(
                        string.IsNullOrWhiteSpace(compositePartName) ? ReadTraceKind.Once : ReadTraceKind.CompositePartOnce,
                        action,
                        compositePartName,
                        true);
                }

                return triggered;
            }

            /// <summary>
            /// 在每次首次按下时切换并返回缓存状态。
            /// </summary>
            public bool Toggle(InputAction action)
            {
                return Toggle(action, null);
            }

            /// <summary>
            /// 组合绑定指定部分在每次首次按下时切换并返回缓存状态。
            /// </summary>
            public bool Toggle(InputAction action, string compositePartName)
            {
                bool pressed = IsPressed(action, compositePartName);
                if (pressed && !_wasPressed)
                {
                    _toggled = !_toggled;
                    RaiseReadTrace(
                        string.IsNullOrWhiteSpace(compositePartName) ? ReadTraceKind.Toggle : ReadTraceKind.CompositePartToggle,
                        action,
                        compositePartName,
                        _toggled);
                }

                _wasPressed = pressed;
                return _toggled;
            }

            /// <summary>
            /// 清零边沿与开关状态。
            /// </summary>
            public void Reset()
            {
                _wasPressed = false;
                _toggled = false;
            }

            /// <summary>
            /// 直接设置开关状态（不改变边沿）。
            /// </summary>
            public void SetToggled(bool value)
            {
                _toggled = value;
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
                    _hash = (RuntimeHelpers.GetHashCode(action) * 397)
                            ^ StringComparer.OrdinalIgnoreCase.GetHashCode(partName);
                }
            }

            public bool Equals(CompositePartKey other)
            {
                return ReferenceEquals(_action, other._action)
                       && string.Equals(_partName, other._partName, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is CompositePartKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return _hash;
            }
        }
    }
}
#endif
