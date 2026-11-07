#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed class InputWatch
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

    private InputAction _anyInputAction;
    private readonly Dictionary<InputDevice, InputContext> _contexts = new Dictionary<InputDevice, InputContext>(8);
    private readonly InputDeviceType _deviceType;
    private readonly InputContext _defaultContext;
    private InputDevice _currentDevice;
    private string _currentProfileId;

    internal InputWatch()
    {
        _deviceType = ResolveDeviceType();
        _defaultContext = IsMobilePlatform() ? InputContext.Touch(_deviceType) : InputContext.KeyboardMouse(_deviceType);
        Current = _defaultContext;
        _currentProfileId = InputProfileCatalog.GetId(Current.InputProfile);
    }

    public InputContext Current { get; private set; }

    public InputDeviceType CurrentDeviceType => Current.DeviceType;
    public InputType CurrentInputType => Current.InputType;
    public InputProfile CurrentInputProfile => Current.InputProfile;
    public string CurrentControlScheme => Current.ControlScheme;
    public string CurrentProfileId => _currentProfileId;
    public bool IsNavigationInput => Current.InputType == InputType.Gamepad || Current.InputType == InputType.Joystick;
    public bool IsTouchInput => Current.InputType == InputType.Touch;
    public bool IsKeyboardMouseInput => Current.InputType == InputType.KeyboardMouse;

    public event Action<InputContext> OnContextChanged;
    public event Action<InputContext> OnInputActivity;
    public event Action<InputDeviceType> OnDeviceTypeChanged;
    public event Action<InputType> OnInputTypeChanged;
    public event Action<InputProfile> OnInputProfileChanged;

    internal void Initialize()
    {
        CreateAnyInputAction();
        _anyInputAction.Enable();
        InputSystem.onDeviceChange += OnDeviceChange;
        SetCurrentContext(ResolveInitialContext(), null, true);
    }

    internal void Shutdown()
    {
        InputSystem.onDeviceChange -= OnDeviceChange;
        if (_anyInputAction != null)
        {
            _anyInputAction.performed -= OnAnyInputPerformed;
            _anyInputAction.Disable();
            _anyInputAction.Dispose();
            _anyInputAction = null;
        }
        _contexts.Clear();
        _currentDevice = null;
        Current = _defaultContext;
        _currentProfileId = InputProfileCatalog.GetId(Current.InputProfile);
        OnContextChanged = null;
        OnInputActivity = null;
        OnDeviceTypeChanged = null;
        OnInputTypeChanged = null;
        OnInputProfileChanged = null;
    }

    /// <summary>
    /// 构建指定设备的输入上下文。只组装现有字符串与值类型，不产生托管分配；
    /// 高频路径仍应使用内部的 <see cref="ResolveContext"/>，它按设备缓存结果。
    /// </summary>
    public InputContext BuildContext(InputDevice device)
    {
        if (device == null)
        {
            return _defaultContext;
        }

        InputType inputType = ResolveInputType(device);
        if (inputType == InputType.KeyboardMouse)
        {
            return InputContext.KeyboardMouse(_deviceType, device);
        }

        if (inputType == InputType.Touch)
        {
            return InputContext.Touch(_deviceType, device);
        }

        return new InputContext(
            _deviceType,
            inputType,
            ResolveInputProfile(device, inputType),
            ResolveControlScheme(inputType),
            device.deviceId,
            GetDeviceName(device));
    }

    /// <summary>
    /// 获取设备上下文，首次访问后按设备缓存。设备变化时仅使该设备的缓存失效。
    /// </summary>
    internal InputContext ResolveContext(InputDevice device)
    {
        if (device == null)
        {
            return _defaultContext;
        }

        if (ReferenceEquals(device, _currentDevice)) return Current;
        if (_contexts.TryGetValue(device, out InputContext context)) return context;
        context = BuildContext(device);
        _contexts.Add(device, context);
        return context;
    }

    private void CreateAnyInputAction()
    {
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

    private void OnAnyInputPerformed(InputAction.CallbackContext context)
    {
        InputControl control = context.control;
        if (!IsMeaningfulControl(control))
        {
            return;
        }

        InputDevice device = control.device;
        if (!ReferenceEquals(device, _currentDevice))
            SetCurrentContext(ResolveContext(device), device);
        OnInputActivity?.Invoke(Current);
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device == null)
        {
            return;
        }

        _contexts.Remove(device);
        if (ReferenceEquals(device, _currentDevice)) _currentDevice = null;

        switch (change)
        {
            case InputDeviceChange.Added:
            case InputDeviceChange.Reconnected:
                if (ShouldPromoteAddedDevice(device))
                {
                    SetCurrentContext(ResolveContext(device), device);
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

    private void SetCurrentContext(InputContext context, InputDevice device = null, bool forceEmit = false)
    {
        _currentDevice = device;
        InputContext previousContext = Current;
        if (!forceEmit && previousContext.Equals(context))
        {
            return;
        }

        Current = context;
        _currentProfileId = InputProfileCatalog.GetId(context.InputProfile);
        OnContextChanged?.Invoke(context);

        if (forceEmit || previousContext.DeviceType != context.DeviceType)
        {
            OnDeviceTypeChanged?.Invoke(context.DeviceType);
        }

        if (forceEmit || previousContext.InputType != context.InputType)
        {
            OnInputTypeChanged?.Invoke(context.InputType);
        }

        if (forceEmit || previousContext.InputProfile != context.InputProfile)
        {
            OnInputProfileChanged?.Invoke(context.InputProfile);
        }
    }

    private InputContext ResolveInitialContext()
    {
        if (TryFindPreferredDevice(out InputDevice device))
        {
            return ResolveContext(device);
        }

        if (_defaultContext.InputType == InputType.Touch)
        {
            if (Touchscreen.current != null && Touchscreen.current.added)
            {
                return ResolveContext(Touchscreen.current);
            }

            return _defaultContext;
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

        return _defaultContext;
    }

    private bool TryFindPreferredDevice(out InputDevice device)
    {
        InputDevice fallbackJoystick = null;
        var devices = InputSystem.devices;
        for (int i = 0; i < devices.Count; i++)
        {
            InputDevice candidate = devices[i];
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

    private InputDeviceType ResolveDeviceType()
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

    private bool ShouldPromoteAddedDevice(InputDevice device)
    {
        if (ResolveInputType(device) == InputType.Unknown)
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

    private InputType ResolveInputType(InputDevice device)
    {
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

    private string ResolveControlScheme(InputType inputType)
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

    private bool IsMeaningfulControl(InputControl control)
    {
        if (control == null)
        {
            return false;
        }

        InputDevice device = control.device;
        if (device == null)
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
            case Vector2Control vector2:
                return vector2.ReadValue().sqrMagnitude >= StickActivationThreshold * StickActivationThreshold;
            case AxisControl axis:
                return Mathf.Abs(axis.ReadValue()) >= AxisActivationThreshold;
            default:
                return !control.noisy;
        }
    }

    private bool IsGamepadLike(InputDevice device)
    {
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

    private InputProfile ResolveInputProfile(InputDevice device, InputType inputType)
    {
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

    private bool DeviceTextContains(InputDevice device, string token)
    {
        return InputStringUtility.ContainsIgnoreCase(device.layout, token)
               || InputStringUtility.ContainsIgnoreCase(device.displayName, token)
               || InputStringUtility.ContainsIgnoreCase(device.name, token)
               || InputStringUtility.ContainsIgnoreCase(device.description.manufacturer, token)
               || InputStringUtility.ContainsIgnoreCase(device.description.product, token)
               || InputStringUtility.ContainsIgnoreCase(device.description.interfaceName, token);
    }

    private string GetDeviceName(InputDevice device)
    {
        return !string.IsNullOrWhiteSpace(device.displayName) ? device.displayName : device.name;
    }

    private bool IsMobilePlatform()
    {
#if UNITY_ANDROID || UNITY_IOS
        return true;
#else
        return Application.isMobilePlatform;
#endif
    }

    private bool IsTabletPlatform()
    {
#if UNITY_IOS || UNITY_ANDROID
        float minSide = Mathf.Min(Screen.width, Screen.height);
        float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
        return minSide / dpi >= 3.6f;
#else
        return false;
#endif
    }

    private bool IsSteamDeckPlatform()
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
    /// 当前输入上下文。设备连接期间由 <see cref="InputWatch"/> 缓存，事件只传引用与稳定字符串。
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
#endif
