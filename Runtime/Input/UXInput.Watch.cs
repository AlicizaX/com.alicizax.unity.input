#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public static partial class UXInput
{
    public static class Watch
    {
        private const float AxisActivationThreshold = 0.5f;
        private const float StickActivationThreshold = 0.25f;
        private const string KeyboardMouseScheme = "KeyboardMouse";
        private const string TouchScheme = "Touch";
        private const string GamepadScheme = "Gamepad";
        private const string JoystickScheme = "Joystick";
        private const string UnknownScheme = "Unknown";
        private const string DefaultKeyboardMouseName = "Keyboard&Mouse";
        private const string DefaultTouchName = "Touchscreen";

        private static InputAction _anyInputAction;
        private static bool _initialized;

        // 设备描述在连接期间是稳定不变量：按设备缓存上下文，输入事件只做引用比较。
        private static InputDevice _cachedDevice;
        private static int _cachedDeviceId = -1;
        private static InputContext _cachedContext;

        public static InputContext Current { get; private set; } = InputContext.KeyboardMouse(ResolveDeviceType());
        private static string _currentProfileId = InputProfileCatalog.GetId(Current.InputProfile);

        public static InputDeviceType CurrentDeviceType => Current.DeviceType;
        public static InputType CurrentInputType => Current.InputType;
        public static InputProfile CurrentInputProfile => Current.InputProfile;
        public static string CurrentControlScheme => Current.ControlScheme;
        public static string CurrentProfileId => _currentProfileId;
        public static bool IsNavigationInput => Current.InputType == InputType.Gamepad || Current.InputType == InputType.Joystick;
        public static bool IsTouchInput => Current.InputType == InputType.Touch;
        public static bool IsKeyboardMouseInput => Current.InputType == InputType.KeyboardMouse;

        public static event Action<InputContext> OnContextChanged;
        public static event Action<InputContext> OnInputActivity;
        public static event Action<InputDeviceType> OnDeviceTypeChanged;
        public static event Action<InputType> OnInputTypeChanged;
        public static event Action<InputProfile> OnInputProfileChanged;

        internal static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            SetCurrentContext(ResolveInitialContext(), true);
            CreateAnyInputAction();
            _anyInputAction.Enable();

            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        internal static void Shutdown(bool clearEventListeners = false)
        {
            InputSystem.onDeviceChange -= OnDeviceChange;

            if (_anyInputAction != null)
            {
                _anyInputAction.performed -= OnAnyInputPerformed;
                _anyInputAction.Disable();
                _anyInputAction.Dispose();
                _anyInputAction = null;
            }

            ResetStaticState(clearEventListeners);
        }

        internal static void ResetForPlayMode()
        {
            Shutdown(true);
        }

        /// <summary>
        /// 构建指定设备的输入上下文。只组装现有字符串与值类型，不产生托管分配；
        /// 高频路径仍应使用内部的 <see cref="ResolveContext"/>，它按设备缓存结果。
        /// </summary>
        public static InputContext BuildContext(InputDevice device)
        {
            if (device == null)
            {
                return ResolvePlatformDefaultContext();
            }

            InputType inputType = ResolveInputType(device);
            if (inputType == InputType.KeyboardMouse)
            {
                return InputContext.KeyboardMouse(ResolveDeviceType(), device);
            }

            if (inputType == InputType.Touch)
            {
                return InputContext.Touch(ResolveDeviceType(), device);
            }

            return new InputContext(
                ResolveDeviceType(),
                inputType,
                ResolveInputProfile(device, inputType),
                ResolveControlScheme(inputType),
                device.deviceId,
                GetDeviceName(device));
        }

        /// <summary>
        /// 获取设备上下文，首次访问后按设备缓存。设备增删/重连时缓存失效。
        /// </summary>
        internal static InputContext ResolveContext(InputDevice device)
        {
            if (device == null)
            {
                return ResolvePlatformDefaultContext();
            }

            if (ReferenceEquals(device, _cachedDevice) && device.deviceId == _cachedDeviceId)
            {
                return _cachedContext;
            }

            InputContext context = BuildContext(device);
            _cachedDevice = device;
            _cachedDeviceId = device.deviceId;
            _cachedContext = context;
            return context;
        }

        private static void CreateAnyInputAction()
        {
            if (_anyInputAction != null)
            {
                return;
            }

            _anyInputAction = new InputAction("UXInput.Watch.AnyInput", InputActionType.PassThrough);
            _anyInputAction.AddBinding("<Keyboard>/anyKey");
            _anyInputAction.AddBinding("<Mouse>/delta");
            _anyInputAction.AddBinding("<Mouse>/scroll");
            _anyInputAction.AddBinding("<Mouse>/leftButton");
            _anyInputAction.AddBinding("<Mouse>/rightButton");
            _anyInputAction.AddBinding("<Mouse>/middleButton");
            _anyInputAction.AddBinding("<Touchscreen>/primaryTouch/press");
            _anyInputAction.AddBinding("<Touchscreen>/primaryTouch/delta");
            _anyInputAction.AddBinding("<Pen>/tip");
            _anyInputAction.AddBinding("<Gamepad>/buttonSouth");
            _anyInputAction.AddBinding("<Gamepad>/buttonNorth");
            _anyInputAction.AddBinding("<Gamepad>/buttonEast");
            _anyInputAction.AddBinding("<Gamepad>/buttonWest");
            _anyInputAction.AddBinding("<Gamepad>/startButton");
            _anyInputAction.AddBinding("<Gamepad>/selectButton");
            _anyInputAction.AddBinding("<Gamepad>/leftShoulder");
            _anyInputAction.AddBinding("<Gamepad>/rightShoulder");
            _anyInputAction.AddBinding("<Gamepad>/leftTrigger");
            _anyInputAction.AddBinding("<Gamepad>/rightTrigger");
            _anyInputAction.AddBinding("<Gamepad>/leftStick");
            _anyInputAction.AddBinding("<Gamepad>/rightStick");
            _anyInputAction.AddBinding("<Gamepad>/dpad");
            _anyInputAction.AddBinding("<Joystick>/trigger");
            _anyInputAction.AddBinding("<Joystick>/stick");
            _anyInputAction.performed += OnAnyInputPerformed;
        }

        private static void OnAnyInputPerformed(InputAction.CallbackContext context)
        {
            InputControl control = context.control;
            if (!IsMeaningfulControl(control))
            {
                return;
            }

            InputDevice device = control.device;
            if (device == null)
            {
                return;
            }

            SetCurrentContext(ResolveContext(device));
            OnInputActivity?.Invoke(Current);
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device == null)
            {
                return;
            }

            InvalidateContextCache();

            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                    if (ShouldPromoteAddedDevice(device))
                    {
                        SetCurrentContext(ResolveContext(device));
                    }

                    break;
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                    if (device.deviceId == Current.DeviceId)
                    {
                        SetCurrentContext(ResolveInitialContext());
                    }

                    break;
            }
        }

        private static void SetCurrentContext(InputContext context, bool forceEmit = false)
        {
            InputContext resolvedContext = context.IsValid ? context : ResolvePlatformDefaultContext();
            InputContext previousContext = Current;
            if (!forceEmit && previousContext.Equals(resolvedContext))
            {
                return;
            }

            Current = resolvedContext;
            _currentProfileId = InputProfileCatalog.GetId(resolvedContext.InputProfile);
            OnContextChanged?.Invoke(resolvedContext);

            if (forceEmit || previousContext.DeviceType != resolvedContext.DeviceType)
            {
                OnDeviceTypeChanged?.Invoke(resolvedContext.DeviceType);
            }

            if (forceEmit || previousContext.InputType != resolvedContext.InputType)
            {
                OnInputTypeChanged?.Invoke(resolvedContext.InputType);
            }

            if (forceEmit || previousContext.InputProfile != resolvedContext.InputProfile)
            {
                OnInputProfileChanged?.Invoke(resolvedContext.InputProfile);
            }
        }

        private static void InvalidateContextCache()
        {
            _cachedDevice = null;
            _cachedDeviceId = -1;
            _cachedContext = default;
        }

        private static InputContext ResolveInitialContext()
        {
            if (TryFindPreferredDevice(out InputDevice device))
            {
                return ResolveContext(device);
            }

            if (IsMobilePlatform())
            {
                if (Touchscreen.current != null && Touchscreen.current.added)
                {
                    return ResolveContext(Touchscreen.current);
                }

                return InputContext.Touch(ResolveDeviceType());
            }

            if (Keyboard.current != null && Keyboard.current.added)
            {
                return ResolveContext(Keyboard.current);
            }

            if (Mouse.current != null && Mouse.current.added)
            {
                return ResolveContext(Mouse.current);
            }

            if (Touchscreen.current != null && Touchscreen.current.added)
            {
                return ResolveContext(Touchscreen.current);
            }

            return ResolvePlatformDefaultContext();
        }

        private static bool TryFindPreferredDevice(out InputDevice device)
        {
            InputDevice fallbackJoystick = null;
            var devices = InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                InputDevice candidate = devices[i];
                if (candidate == null || !candidate.added)
                {
                    continue;
                }

                if (IsGamepadLike(candidate))
                {
                    device = candidate;
                    return true;
                }

                if (fallbackJoystick == null && candidate is Joystick)
                {
                    fallbackJoystick = candidate;
                }
            }

            device = fallbackJoystick;
            return fallbackJoystick != null;
        }

        private static InputContext ResolvePlatformDefaultContext()
        {
            InputDeviceType deviceType = ResolveDeviceType();
            return IsMobilePlatform() ? InputContext.Touch(deviceType) : InputContext.KeyboardMouse(deviceType);
        }

        private static InputDeviceType ResolveDeviceType()
        {
#if UNITY_SWITCH
            return InputDeviceType.Switch;
#elif UNITY_PS4 || UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE
            return InputDeviceType.Console;
#elif UNITY_ANDROID || UNITY_IOS
            return IsTabletPlatform() ? InputDeviceType.Tablet : InputDeviceType.Phone;
#else
            if (IsSteamDeckPlatform())
            {
                return InputDeviceType.SteamDeck;
            }

            if (Application.isMobilePlatform)
            {
                return IsTabletPlatform() ? InputDeviceType.Tablet : InputDeviceType.Phone;
            }

            return InputDeviceType.PC;
#endif
        }

        private static bool ShouldPromoteAddedDevice(InputDevice device)
        {
            if (device == null || ResolveInputType(device) == InputType.Unknown)
            {
                return false;
            }

            if (Current.InputType == InputType.Unknown)
            {
                return true;
            }

            return (IsGamepadLike(device) || device is Joystick) &&
                   (Current.InputType == InputType.KeyboardMouse || Current.InputType == InputType.Touch);
        }

        private static InputType ResolveInputType(InputDevice device)
        {
            if (device == null)
            {
                return InputType.Unknown;
            }

            if (device is Keyboard || device is Mouse)
            {
                return InputType.KeyboardMouse;
            }

            if (device is Touchscreen || device is Pen)
            {
                return InputType.Touch;
            }

            if (IsGamepadLike(device))
            {
                return InputType.Gamepad;
            }

            if (device is Joystick)
            {
                return InputType.Joystick;
            }

            return InputType.Unknown;
        }

        private static string ResolveControlScheme(InputType inputType)
        {
            switch (inputType)
            {
                case InputType.Touch:
                    return TouchScheme;
                case InputType.KeyboardMouse:
                    return KeyboardMouseScheme;
                case InputType.Gamepad:
                    return GamepadScheme;
                case InputType.Joystick:
                    return JoystickScheme;
                default:
                    return UnknownScheme;
            }
        }

        private static bool IsMeaningfulControl(InputControl control)
        {
            if (control == null)
            {
                return false;
            }

            InputDevice device = control.device;
            if (device == null || !IsRelevantControlDevice(device))
            {
                return false;
            }

            if (control.synthetic && !(device is Keyboard keyboard && control == keyboard.anyKey))
            {
                return false;
            }

            switch (control)
            {
                case ButtonControl button:
                    return button.IsPressed();
                case StickControl stick:
                    return stick.ReadValue().sqrMagnitude >= StickActivationThreshold * StickActivationThreshold;
                case Vector2Control vector2:
                    return vector2.ReadValue().sqrMagnitude >= StickActivationThreshold * StickActivationThreshold;
                case AxisControl axis:
                    return Mathf.Abs(axis.ReadValue()) >= AxisActivationThreshold;
                default:
                    return !control.noisy;
            }
        }

        private static bool IsRelevantControlDevice(InputDevice device)
        {
            // AnyInput 只绑定到这些设备的具名控件，无需再做布局字符串匹配。
            return device is Keyboard
                   || device is Mouse
                   || device is Touchscreen
                   || device is Pen
                   || device is Gamepad
                   || device is Joystick;
        }

        private static bool IsGamepadLike(InputDevice device)
        {
            if (device == null)
            {
                return false;
            }

            if (device is Gamepad)
            {
                return true;
            }

            string layout = device.layout;
            if (string.IsNullOrWhiteSpace(layout)
                || InputStringUtility.ContainsIgnoreCase(layout, "Mouse")
                || InputStringUtility.ContainsIgnoreCase(layout, "Touch")
                || InputStringUtility.ContainsIgnoreCase(layout, "Pen"))
            {
                return false;
            }

            return InputStringUtility.ContainsIgnoreCase(layout, "Gamepad")
                   || InputStringUtility.ContainsIgnoreCase(layout, "Controller")
                   || InputStringUtility.ContainsIgnoreCase(layout, "JoyCon")
                   || InputStringUtility.ContainsIgnoreCase(layout, "DualShock")
                   || InputStringUtility.ContainsIgnoreCase(layout, "DualSense");
        }

        private static InputProfile ResolveInputProfile(InputDevice device, InputType inputType)
        {
            if (device == null)
            {
                return InputProfile.Unknown;
            }

            switch (inputType)
            {
                case InputType.KeyboardMouse:
                    return InputProfile.KeyboardMouse;
                case InputType.Touch:
                    return InputProfile.Touch;
                case InputType.Gamepad:
                case InputType.Joystick:
                    break;
                default:
                    return InputProfile.Unknown;
            }

            if (DeviceTextContains(device, "Steam Deck"))
            {
                return InputProfile.SteamDeck;
            }

            if (DeviceTextContains(device, "Steam"))
            {
                return InputProfile.SteamController;
            }

            if (DeviceTextContains(device, "DualShock")
                || DeviceTextContains(device, "DualSense")
                || DeviceTextContains(device, "PlayStation"))
            {
                return InputProfile.PlayStation;
            }

            if (DeviceTextContains(device, "Xbox"))
            {
                return InputProfile.Xbox;
            }

            if (DeviceTextContains(device, "Switch")
                || DeviceTextContains(device, "Joy-Con")
                || DeviceTextContains(device, "JoyCon"))
            {
                return InputProfile.Switch;
            }

            return inputType == InputType.Joystick ? InputProfile.GenericJoystick : InputProfile.GenericGamepad;
        }

        private static bool DeviceTextContains(InputDevice device, string token)
        {
            return InputStringUtility.ContainsIgnoreCase(device.layout, token)
                   || InputStringUtility.ContainsIgnoreCase(device.displayName, token)
                   || InputStringUtility.ContainsIgnoreCase(device.name, token)
                   || InputStringUtility.ContainsIgnoreCase(device.description.manufacturer, token)
                   || InputStringUtility.ContainsIgnoreCase(device.description.product, token)
                   || InputStringUtility.ContainsIgnoreCase(device.description.interfaceName, token);
        }

        private static string GetDeviceName(InputDevice device)
        {
            return !string.IsNullOrWhiteSpace(device.displayName) ? device.displayName : device.name;
        }

        private static bool IsMobilePlatform()
        {
#if UNITY_ANDROID || UNITY_IOS
            return true;
#else
            return Application.isMobilePlatform;
#endif
        }

        private static bool IsTabletPlatform()
        {
#if UNITY_IOS || UNITY_ANDROID
            float minSide = Mathf.Min(Screen.width, Screen.height);
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            return minSide / dpi >= 3.6f;
#else
            return false;
#endif
        }

        private static bool IsSteamDeckPlatform()
        {
#if UNITY_STANDALONE_LINUX
            string deviceModel = SystemInfo.deviceModel;
            string deviceName = SystemInfo.deviceName;
            return InputStringUtility.ContainsIgnoreCase(deviceModel, "Steam Deck") ||
                   InputStringUtility.ContainsIgnoreCase(deviceName, "Steam Deck");
#else
            return false;
#endif
        }

        private static void ResetStaticState(bool clearEventListeners)
        {
            InvalidateContextCache();
            Current = InputContext.KeyboardMouse(ResolveDeviceType());
            _currentProfileId = InputProfileCatalog.GetId(Current.InputProfile);
            _initialized = false;

            if (clearEventListeners)
            {
                OnContextChanged = null;
                OnInputActivity = null;
                OnDeviceTypeChanged = null;
                OnInputTypeChanged = null;
                OnInputProfileChanged = null;
            }
        }

        public enum InputDeviceType : byte
        {
            Unknown = 0,
            Phone = 1,
            Tablet = 2,
            PC = 3,
            Console = 4,
            Handheld = 5,
            SteamDeck = 6,
            Switch = 7
        }

        public enum InputType : byte
        {
            Unknown = 0,
            Touch = 1,
            KeyboardMouse = 2,
            Gamepad = 3,
            Joystick = 4
        }

        public enum InputProfile : byte
        {
            Unknown = 0,
            Touch = 1,
            KeyboardMouse = 2,
            GenericGamepad = 3,
            GenericJoystick = 4,
            Xbox = 5,
            PlayStation = 6,
            Switch = 7,
            SteamDeck = 8,
            SteamController = 9
        }

        /// <summary>
        /// 当前输入上下文。设备连接期间由 <see cref="Watch"/> 缓存，事件只传引用与稳定字符串。
        /// </summary>
        public readonly struct InputContext : IEquatable<InputContext>
        {
            public readonly InputDeviceType DeviceType;
            public readonly InputType InputType;
            public readonly InputProfile InputProfile;
            public readonly string ControlScheme;
            public readonly int DeviceId;
            public readonly string DeviceName;

            public bool IsValid => DeviceType != InputDeviceType.Unknown && InputType != InputType.Unknown;

            public InputContext(
                InputDeviceType deviceType,
                InputType inputType,
                InputProfile inputProfile,
                string controlScheme,
                int deviceId,
                string deviceName)
            {
                DeviceType = deviceType;
                InputType = inputType;
                InputProfile = inputProfile;
                ControlScheme = controlScheme ?? string.Empty;
                DeviceId = deviceId;
                DeviceName = deviceName ?? string.Empty;
            }

            public static InputContext KeyboardMouse(InputDeviceType deviceType)
            {
                return new InputContext(
                    deviceType,
                    InputType.KeyboardMouse,
                    InputProfile.KeyboardMouse,
                    KeyboardMouseScheme,
                    -1,
                    DefaultKeyboardMouseName);
            }

            public static InputContext KeyboardMouse(InputDeviceType deviceType, InputDevice device)
            {
                string deviceName = device == null || string.IsNullOrWhiteSpace(device.displayName)
                    ? DefaultKeyboardMouseName
                    : device.displayName;

                return new InputContext(
                    deviceType,
                    InputType.KeyboardMouse,
                    InputProfile.KeyboardMouse,
                    KeyboardMouseScheme,
                    device != null ? device.deviceId : -1,
                    deviceName);
            }

            public static InputContext Touch(InputDeviceType deviceType)
            {
                return new InputContext(
                    deviceType,
                    InputType.Touch,
                    InputProfile.Touch,
                    TouchScheme,
                    -1,
                    DefaultTouchName);
            }

            public static InputContext Touch(InputDeviceType deviceType, InputDevice device)
            {
                string deviceName = device == null || string.IsNullOrWhiteSpace(device.displayName)
                    ? DefaultTouchName
                    : device.displayName;

                return new InputContext(
                    deviceType,
                    InputType.Touch,
                    InputProfile.Touch,
                    TouchScheme,
                    device != null ? device.deviceId : -1,
                    deviceName);
            }

            public bool Equals(InputContext other)
            {
                return DeviceType == other.DeviceType
                       && InputType == other.InputType
                       && InputProfile == other.InputProfile
                       && DeviceId == other.DeviceId
                       && string.Equals(ControlScheme, other.ControlScheme, StringComparison.Ordinal)
                       && string.Equals(DeviceName, other.DeviceName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is InputContext other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)DeviceType;
                    hash = (hash * 397) ^ (int)InputType;
                    hash = (hash * 397) ^ (int)InputProfile;
                    hash = (hash * 397) ^ DeviceId;
                    hash = (hash * 397) ^ (ControlScheme != null ? ControlScheme.GetHashCode() : 0);
                    hash = (hash * 397) ^ (DeviceName != null ? DeviceName.GetHashCode() : 0);
                    return hash;
                }
            }
        }
    }
}
#endif
