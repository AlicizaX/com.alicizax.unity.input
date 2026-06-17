#if INPUTSYSTEM_SUPPORT
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;

public static partial class UXInput
{
    public static class Watch
    {
        private const float AxisActivationThreshold = 0.5f;
        private const float StickActivationThreshold = 0.25f;
        private const int InitialDeviceProbeFrames = 30;
        private const string KeyboardMouseScheme = "KeyboardMouse";
        private const string TouchScheme = "Touch";
        private const string GamepadScheme = "Gamepad";
        private const string JoystickScheme = "Joystick";
        private const string UnknownScheme = "Unknown";
        private const string DefaultKeyboardMouseName = "Keyboard&Mouse";
        private const string DefaultTouchName = "Touchscreen";
        private const string VendorIdField = "vendorId";
        private const string ProductIdField = "productId";

        private static InputAction _anyInputAction;
        private static bool _initialized;
        private static int _initialDeviceProbeFramesRemaining;

        public static InputContext Current { get; private set; } = InputContext.KeyboardMouse(ResolveDeviceType());
        public static InputDeviceType CurrentDeviceType => Current.DeviceType;
        public static InputType CurrentInputType => Current.InputType;
        public static InputProfile CurrentInputProfile => Current.InputProfile;
        public static string CurrentControlScheme => Current.ControlScheme;
        public static bool IsNavigationInput => Current.InputType == InputType.Gamepad || Current.InputType == InputType.Joystick;
        public static bool IsTouchInput => Current.InputType == InputType.Touch;
        public static bool IsKeyboardMouseInput => Current.InputType == InputType.KeyboardMouse;

        public static event Action<InputContext> OnContextChanged;
        public static event Action<InputContext> OnInputActivity;
        public static event Action<InputDeviceType> OnDeviceTypeChanged;
        public static event Action<InputType> OnInputTypeChanged;
        public static event Action<InputProfile> OnInputProfileChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
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

            _initialDeviceProbeFramesRemaining = InitialDeviceProbeFrames;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;
            InputSystem.onAfterUpdate += OnAfterInputUpdate;

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        public static void Dispose()
        {
            if (!_initialized)
            {
                return;
            }

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;

            if (_anyInputAction != null)
            {
                _anyInputAction.performed -= OnAnyInputPerformed;
                _anyInputAction.Disable();
                _anyInputAction.Dispose();
                _anyInputAction = null;
            }

            ResetStaticState(true);
        }

        public static InputContext BuildContext(InputDevice device)
        {
            if (device == null)
            {
                return ResolvePlatformDefaultContext();
            }

            if (IsKeyboardMouseDevice(device))
            {
                return InputContext.KeyboardMouse(ResolveDeviceType(), device);
            }

            if (device is Touchscreen)
            {
                return InputContext.Touch(ResolveDeviceType(), device);
            }

            TryParseVendorProductIds(device.description.capabilities, out int vendorId, out int productId);
            InputDeviceType deviceType = ResolveDeviceType();
            InputType inputType = ResolveInputType(device);
            InputProfile inputProfile = ResolveInputProfile(device, vendorId, productId);
            string controlScheme = ResolveControlScheme(inputType);
            string deviceName = string.IsNullOrWhiteSpace(device.displayName) ? device.name : device.displayName;
            InputDeviceDescription description = device.description;

            return new InputContext(
                deviceType,
                inputType,
                inputProfile,
                controlScheme,
                device.deviceId,
                vendorId,
                productId,
                deviceName,
                device.layout,
                description.interfaceName,
                description.manufacturer,
                description.product);
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

            if (IsInitialProbeActive() && !IsGamepadLike(device) && !(device is Joystick))
            {
                return;
            }

            SetCurrentContext(BuildContext(device));
            OnInputActivity?.Invoke(Current);
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device == null)
            {
                return;
            }

            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Reconnected:
                    if (ShouldPromoteAddedDevice(device))
                    {
                        SetCurrentContext(BuildContext(device));
                    }

                    break;
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                    if (device.deviceId == Current.DeviceId)
                    {
                        SetCurrentContext(ResolveFallbackContext(device.deviceId));
                    }

                    break;
            }
        }

        private static void OnAfterInputUpdate()
        {
            if (_initialDeviceProbeFramesRemaining <= 0)
            {
                InputSystem.onAfterUpdate -= OnAfterInputUpdate;
                return;
            }

            _initialDeviceProbeFramesRemaining--;
            InputContext initialContext = ResolveInitialContext();
            if (ShouldPromoteInitialContext(initialContext))
            {
                SetCurrentContext(initialContext);
            }

            if (Current.InputType == InputType.Gamepad ||
                Current.InputType == InputType.Joystick ||
                _initialDeviceProbeFramesRemaining <= 0)
            {
                _initialDeviceProbeFramesRemaining = 0;
                InputSystem.onAfterUpdate -= OnAfterInputUpdate;
            }
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                Dispose();
            }
        }
#endif

        private static void SetCurrentContext(InputContext context, bool forceEmit = false)
        {
            InputContext resolvedContext = context.IsValid ? context : ResolvePlatformDefaultContext();
            InputContext previousContext = Current;
            if (!forceEmit && previousContext.Equals(resolvedContext))
            {
                return;
            }

            Current = resolvedContext;
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

        private static InputContext ResolveInitialContext()
        {
            InputDevice device;
            if (TryFindInitialGamepadLikeDevice(out device))
            {
                return BuildContext(device);
            }

            if (TryFindInitialJoystick(out device))
            {
                return BuildContext(device);
            }

            if (IsMobilePlatform())
            {
                if (Touchscreen.current != null && Touchscreen.current.added)
                {
                    return BuildContext(Touchscreen.current);
                }

                return InputContext.Touch(ResolveDeviceType());
            }

            if (Keyboard.current != null && Keyboard.current.added)
            {
                return BuildContext(Keyboard.current);
            }

            if (Mouse.current != null && Mouse.current.added)
            {
                return BuildContext(Mouse.current);
            }

            if (Touchscreen.current != null && Touchscreen.current.added)
            {
                return BuildContext(Touchscreen.current);
            }

            return ResolvePlatformDefaultContext();
        }

        private static InputContext ResolveFallbackContext(int removedDeviceId)
        {
            for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = InputSystem.devices[i];
                if (device == null || device.deviceId == removedDeviceId || !device.added || !IsRelevantDevice(device))
                {
                    continue;
                }

                return BuildContext(device);
            }

            return ResolvePlatformDefaultContext();
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
            if (device == null || !IsRelevantDevice(device))
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

        private static bool ShouldPromoteInitialContext(InputContext context)
        {
            if (!context.IsValid || context.DeviceId < 0)
            {
                return false;
            }

            if ((context.InputType == InputType.Gamepad || context.InputType == InputType.Joystick) &&
                Current.InputType != InputType.Gamepad &&
                Current.InputType != InputType.Joystick)
            {
                return true;
            }

            return Current.InputType == InputType.Unknown;
        }

        private static bool TryFindInitialGamepadLikeDevice(out InputDevice device)
        {
            for (int i = 0; i < InputSystem.devices.Count; i++)
            {
                InputDevice candidate = InputSystem.devices[i];
                if (candidate != null && candidate.added && IsGamepadLike(candidate))
                {
                    device = candidate;
                    return true;
                }
            }

            device = null;
            return false;
        }

        private static bool TryFindInitialJoystick(out InputDevice device)
        {
            for (int i = 0; i < InputSystem.devices.Count; i++)
            {
                InputDevice candidate = InputSystem.devices[i];
                if (candidate != null && candidate.added && candidate is Joystick)
                {
                    device = candidate;
                    return true;
                }
            }

            device = null;
            return false;
        }

        private static bool IsInitialProbeActive()
        {
            return _initialDeviceProbeFramesRemaining > 0;
        }

        private static bool IsRelevantDevice(InputDevice device)
        {
            return device is Keyboard ||
                   device is Mouse ||
                   device is Touchscreen ||
                   device is Pen ||
                   device is Joystick ||
                   IsGamepadLike(device);
        }

        private static bool IsKeyboardMouseDevice(InputDevice device)
        {
            return device is Keyboard || device is Mouse;
        }

        private static InputType ResolveInputType(InputDevice device)
        {
            if (device == null)
            {
                return InputType.Unknown;
            }

            if (IsKeyboardMouseDevice(device))
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
            if (control == null || control.device == null || control.synthetic || !IsRelevantDevice(control.device))
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

            string layout = device.layout ?? string.Empty;
            if (ContainsIgnoreCase(layout, "Mouse") ||
                ContainsIgnoreCase(layout, "Touch") ||
                ContainsIgnoreCase(layout, "Pen"))
            {
                return false;
            }

            return ContainsIgnoreCase(layout, "Gamepad") ||
                   ContainsIgnoreCase(layout, "Controller") ||
                   ContainsIgnoreCase(layout, "JoyCon") ||
                   ContainsIgnoreCase(layout, "DualShock") ||
                   ContainsIgnoreCase(layout, "DualSense");
        }

        private static InputProfile ResolveInputProfile(InputDevice device, int vendorId, int productId)
        {
            if (device == null)
            {
                return InputProfile.Unknown;
            }

            if (IsKeyboardMouseDevice(device))
            {
                return InputProfile.KeyboardMouse;
            }

            if (device is Touchscreen || device is Pen)
            {
                return InputProfile.Touch;
            }

            string source = string.Concat(
                device.layout,
                " ",
                device.displayName,
                " ",
                device.name,
                " ",
                device.description.manufacturer,
                " ",
                device.description.product,
                " ",
                device.description.interfaceName);

            if (ContainsIgnoreCase(source, "Steam Deck"))
            {
                return InputProfile.SteamDeck;
            }

            if (ContainsIgnoreCase(source, "Steam"))
            {
                return InputProfile.SteamController;
            }

            if (vendorId == 0x054C ||
                ContainsIgnoreCase(source, "DualShock") ||
                ContainsIgnoreCase(source, "DualSense") ||
                ContainsIgnoreCase(source, "PlayStation"))
            {
                return InputProfile.PlayStation;
            }

            if (vendorId == 0x045E || ContainsIgnoreCase(source, "Xbox"))
            {
                return InputProfile.Xbox;
            }

            if (vendorId == 0x057E ||
                ContainsIgnoreCase(source, "Switch") ||
                ContainsIgnoreCase(source, "Joy-Con") ||
                ContainsIgnoreCase(source, "JoyCon"))
            {
                return InputProfile.Switch;
            }

            if (device is Joystick)
            {
                return InputProfile.GenericJoystick;
            }

            if (IsGamepadLike(device))
            {
                return InputProfile.GenericGamepad;
            }

            return InputProfile.Unknown;
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
            return ContainsIgnoreCase(deviceModel, "Steam Deck") ||
                   ContainsIgnoreCase(deviceName, "Steam Deck");
#else
            return false;
#endif
        }

        private static bool TryParseVendorProductIds(string capabilities, out int vendorId, out int productId)
        {
            vendorId = 0;
            productId = 0;
            if (string.IsNullOrWhiteSpace(capabilities))
            {
                return false;
            }

            TryReadCapabilityInt(capabilities, VendorIdField, out vendorId);
            TryReadCapabilityInt(capabilities, ProductIdField, out productId);
            return vendorId != 0 || productId != 0;
        }

        private static bool TryReadCapabilityInt(string source, string fieldName, out int value)
        {
            value = 0;
            int fieldIndex = FindQuotedField(source, fieldName);
            if (fieldIndex < 0)
            {
                return false;
            }

            int colonIndex = FindColon(source, fieldIndex + fieldName.Length + 2);
            if (colonIndex < 0)
            {
                return false;
            }

            int index = colonIndex + 1;
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index < source.Length && source[index] == '"')
            {
                index++;
            }

            bool isHex = index + 1 < source.Length && source[index] == '0' && (source[index + 1] == 'x' || source[index + 1] == 'X');
            if (isHex)
            {
                index += 2;
            }

            bool hasDigit = false;
            int result = 0;
            while (index < source.Length)
            {
                int digit = isHex ? HexValue(source[index]) : DecimalValue(source[index]);
                if (digit < 0)
                {
                    break;
                }

                hasDigit = true;
                result = isHex ? (result << 4) + digit : result * 10 + digit;
                index++;
            }

            if (!hasDigit)
            {
                return false;
            }

            value = result;
            return true;
        }

        private static int FindQuotedField(string source, string fieldName)
        {
            int end = source.Length - fieldName.Length - 2;
            for (int i = 0; i <= end; i++)
            {
                if (source[i] != '"')
                {
                    continue;
                }

                int nameStart = i + 1;
                int nameEnd = nameStart + fieldName.Length;
                if (nameEnd >= source.Length || source[nameEnd] != '"')
                {
                    continue;
                }

                bool matched = true;
                for (int c = 0; c < fieldName.Length; c++)
                {
                    if (source[nameStart + c] != fieldName[c])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindColon(string source, int startIndex)
        {
            for (int i = startIndex; i < source.Length; i++)
            {
                char value = source[i];
                if (value == ':')
                {
                    return i;
                }

                if (!char.IsWhiteSpace(value))
                {
                    return -1;
                }
            }

            return -1;
        }

        private static int DecimalValue(char value)
        {
            return value >= '0' && value <= '9' ? value - '0' : -1;
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            return -1;
        }

        private static bool ContainsIgnoreCase(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   !string.IsNullOrEmpty(value) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ResetStaticState(bool clearEventListeners)
        {
            Current = InputContext.KeyboardMouse(ResolveDeviceType());
            _initialDeviceProbeFramesRemaining = 0;
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

        public readonly struct InputContext : IEquatable<InputContext>
        {
            public readonly InputDeviceType DeviceType;
            public readonly InputType InputType;
            public readonly InputProfile InputProfile;
            public readonly string ControlScheme;
            public readonly int DeviceId;
            public readonly int VendorId;
            public readonly int ProductId;
            public readonly string DeviceName;
            public readonly string Layout;
            public readonly string InterfaceName;
            public readonly string Manufacturer;
            public readonly string Product;

            public bool IsValid => DeviceType != InputDeviceType.Unknown && InputType != InputType.Unknown;

            public InputContext(
                InputDeviceType deviceType,
                InputType inputType,
                InputProfile inputProfile,
                string controlScheme,
                int deviceId,
                int vendorId,
                int productId,
                string deviceName,
                string layout,
                string interfaceName,
                string manufacturer,
                string product)
            {
                DeviceType = deviceType;
                InputType = inputType;
                InputProfile = inputProfile;
                ControlScheme = controlScheme ?? string.Empty;
                DeviceId = deviceId;
                VendorId = vendorId;
                ProductId = productId;
                DeviceName = deviceName ?? string.Empty;
                Layout = layout ?? string.Empty;
                InterfaceName = interfaceName ?? string.Empty;
                Manufacturer = manufacturer ?? string.Empty;
                Product = product ?? string.Empty;
            }

            public static InputContext KeyboardMouse(InputDeviceType deviceType)
            {
                return new InputContext(
                    deviceType,
                    InputType.KeyboardMouse,
                    InputProfile.KeyboardMouse,
                    KeyboardMouseScheme,
                    -1,
                    0,
                    0,
                    DefaultKeyboardMouseName,
                    KeyboardMouseScheme,
                    string.Empty,
                    string.Empty,
                    string.Empty);
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
                    0,
                    0,
                    deviceName,
                    device != null ? device.layout : KeyboardMouseScheme,
                    device != null ? device.description.interfaceName : string.Empty,
                    device != null ? device.description.manufacturer : string.Empty,
                    device != null ? device.description.product : string.Empty);
            }

            public static InputContext Touch(InputDeviceType deviceType)
            {
                return new InputContext(
                    deviceType,
                    InputType.Touch,
                    InputProfile.Touch,
                    TouchScheme,
                    -1,
                    0,
                    0,
                    DefaultTouchName,
                    TouchScheme,
                    string.Empty,
                    string.Empty,
                    string.Empty);
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
                    0,
                    0,
                    deviceName,
                    device != null ? device.layout : TouchScheme,
                    device != null ? device.description.interfaceName : string.Empty,
                    device != null ? device.description.manufacturer : string.Empty,
                    device != null ? device.description.product : string.Empty);
            }

            public bool Equals(InputContext other)
            {
                return DeviceType == other.DeviceType &&
                       InputType == other.InputType &&
                       InputProfile == other.InputProfile &&
                       DeviceId == other.DeviceId &&
                       VendorId == other.VendorId &&
                       ProductId == other.ProductId &&
                       string.Equals(ControlScheme, other.ControlScheme, StringComparison.Ordinal) &&
                       string.Equals(DeviceName, other.DeviceName, StringComparison.Ordinal) &&
                       string.Equals(Layout, other.Layout, StringComparison.Ordinal) &&
                       string.Equals(InterfaceName, other.InterfaceName, StringComparison.Ordinal) &&
                       string.Equals(Manufacturer, other.Manufacturer, StringComparison.Ordinal) &&
                       string.Equals(Product, other.Product, StringComparison.Ordinal);
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
                    hash = (hash * 397) ^ VendorId;
                    hash = (hash * 397) ^ ProductId;
                    hash = (hash * 397) ^ (ControlScheme != null ? ControlScheme.GetHashCode() : 0);
                    hash = (hash * 397) ^ (DeviceName != null ? DeviceName.GetHashCode() : 0);
                    hash = (hash * 397) ^ (Layout != null ? Layout.GetHashCode() : 0);
                    hash = (hash * 397) ^ (InterfaceName != null ? InterfaceName.GetHashCode() : 0);
                    hash = (hash * 397) ^ (Manufacturer != null ? Manufacturer.GetHashCode() : 0);
                    hash = (hash * 397) ^ (Product != null ? Product.GetHashCode() : 0);
                    return hash;
                }
            }
        }
    }
}
#endif
