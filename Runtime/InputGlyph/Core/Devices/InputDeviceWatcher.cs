#if INPUTSYSTEM_SUPPORT
using System;
using AlicizaX;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;

public static class InputDeviceWatcher
{
    private const float SameProfileDebounceWindow = 0.15f;
    private const float AxisActivationThreshold = 0.5f;
    private const float StickActivationThreshold = 0.25f;
    private const int InitialDeviceProbeFrames = 30;
    private const int InitialContextCapacity = 16;
    private const string DefaultKeyboardDeviceName = "Keyboard&Mouse";
    private const string DefaultKeyboardLayout = "KeyboardMouse";
    private const string KeyboardScheme = "KeyboardMouse";
    private const string GamepadScheme = "Gamepad";
    private const string VendorIdField = "vendorId";
    private const string ProductIdField = "productId";

    public static string CurrentProfileId { get; private set; } = InputGlyphProfileIds.KeyboardMouse;
    public static string CurrentDeviceName { get; private set; } = DefaultKeyboardDeviceName;
    public static int CurrentDeviceId { get; private set; } = -1;
    public static int CurrentVendorId { get; private set; }
    public static int CurrentProductId { get; private set; }
    public static InputGlyphContext CurrentContext { get; private set; } = CreateDefaultContext();

    private static InputGlyphContext[] ContextCache = new InputGlyphContext[InitialContextCapacity];
    private static int ContextCacheCount;
    private static InputAction _anyInputAction;
    private static float _lastSwitchTime = -Mathf.Infinity;
    private static InputGlyphContext _lastEmittedContext = CreateDefaultContext();
    private static bool _initialized;
    private static int _initialDeviceProbeFramesRemaining;

    public static event Action<string> OnProfileChanged;
    public static event Action<InputGlyphContext> OnDeviceContextChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        if (IsMobilePlatform() || _initialized)
        {
            return;
        }

        _initialized = true;
        SetCurrentContext(ResolveInitialContext(), true);

        _anyInputAction = new InputAction("AnyDevice", InputActionType.PassThrough);
        _anyInputAction.AddBinding("<Keyboard>/anyKey");
        _anyInputAction.AddBinding("<Mouse>/leftButton");
        _anyInputAction.AddBinding("<Mouse>/rightButton");
        _anyInputAction.AddBinding("<Mouse>/middleButton");
        _anyInputAction.AddBinding("<Gamepad>/buttonSouth");
        _anyInputAction.AddBinding("<Gamepad>/buttonNorth");
        _anyInputAction.AddBinding("<Gamepad>/buttonEast");
        _anyInputAction.AddBinding("<Gamepad>/buttonWest");
        _anyInputAction.AddBinding("<Gamepad>/startButton");
        _anyInputAction.AddBinding("<Gamepad>/selectButton");
        _anyInputAction.AddBinding("<Gamepad>/leftStick");
        _anyInputAction.AddBinding("<Gamepad>/rightStick");
        _anyInputAction.AddBinding("<Gamepad>/dpad");
        _anyInputAction.AddBinding("<Gamepad>/leftTrigger");
        _anyInputAction.AddBinding("<Gamepad>/rightTrigger");
        _anyInputAction.AddBinding("<Joystick>/trigger");
        _anyInputAction.AddBinding("<Joystick>/stick");
        _anyInputAction.performed += OnAnyInputPerformed;
        _anyInputAction.Enable();

        InputSystem.onDeviceChange += OnDeviceChange;
        _initialDeviceProbeFramesRemaining = InitialDeviceProbeFrames;
        InputSystem.onAfterUpdate += OnAfterInputUpdate;
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
    }

    public static void SetProfileDatabase(InputGlyphDatabase database)
    {
        InputGlyphService.SetDatabase(database);
        RebuildCurrentContextProfile();
    }

    public static InputGlyphContext BuildContext(InputDevice device)
    {
        if (device == null || IsKeyboardMouseDevice(device))
        {
            return CreateKeyboardMouseContext(string.Empty);
        }

        if (TryGetCachedContext(device.deviceId, out InputGlyphContext cachedContext))
        {
            return cachedContext;
        }

        return BuildContext(device, string.Empty, true);
    }

    public static InputGlyphContext BuildContext(InputDevice device, string controlScheme)
    {
        if (device == null || IsKeyboardMouseDevice(device))
        {
            return CreateKeyboardMouseContext(controlScheme);
        }

        return BuildContext(device, controlScheme, false);
    }

    private static InputGlyphContext BuildContext(InputDevice device, string controlScheme, bool cacheContext)
    {
        if (device == null || IsKeyboardMouseDevice(device))
        {
            return CreateKeyboardMouseContext(controlScheme);
        }

        TryParseVendorProductIds(device.description.capabilities, out int vendorId, out int productId);
        InputDeviceDescription description = device.description;
        string deviceName = string.IsNullOrWhiteSpace(device.displayName) ? device.name : device.displayName;
        string resolvedControlScheme = string.IsNullOrWhiteSpace(controlScheme) ? ResolveControlScheme(device) : controlScheme;
        string profileId = InputGlyphService.ResolveProfileId(
            vendorId,
            productId,
            resolvedControlScheme,
            deviceName,
            device.layout,
            description.interfaceName,
            description.manufacturer,
            description.product);

        InputGlyphContext context = new InputGlyphContext(
            device.deviceId,
            vendorId,
            productId,
            profileId,
            resolvedControlScheme,
            deviceName,
            device.layout,
            description.interfaceName,
            description.manufacturer,
            description.product);

        if (cacheContext)
        {
            AddCachedContext(context);
        }

        return context;
    }

    public static void RebuildCurrentContextProfile()
    {
        if (CurrentDeviceId < 0)
        {
            SetCurrentContext(CreateDefaultContext(), true);
            return;
        }

        InputDevice device = InputSystem.GetDeviceById(CurrentDeviceId);
        if (device == null)
        {
            SetCurrentContext(CreateDefaultContext(), true);
            return;
        }

        RemoveCachedContext(CurrentDeviceId);
        SetCurrentContext(BuildContext(device), true);
    }

    private static bool IsMobilePlatform()
    {
#if UNITY_ANDROID || UNITY_IOS
        return true;
#else
        return Application.isMobilePlatform;
#endif
    }

#if UNITY_EDITOR
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Dispose();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }
    }
#endif

    public static void Dispose()
    {
        if (!_initialized)
        {
            return;
        }

        if (_anyInputAction != null)
        {
            _anyInputAction.performed -= OnAnyInputPerformed;
            _anyInputAction.Disable();
            _anyInputAction.Dispose();
            _anyInputAction = null;
        }

        InputSystem.onDeviceChange -= OnDeviceChange;
        InputSystem.onAfterUpdate -= OnAfterInputUpdate;
        Array.Clear(ContextCache, 0, ContextCacheCount);
        ContextCacheCount = 0;

        ApplyContext(CreateDefaultContext(), false);
        _lastEmittedContext = CurrentContext;
        _lastSwitchTime = -Mathf.Infinity;
        _initialDeviceProbeFramesRemaining = 0;
        OnProfileChanged = null;
        OnDeviceContextChanged = null;
        _initialized = false;
    }

    private static void OnAfterInputUpdate()
    {
        if (_initialDeviceProbeFramesRemaining <= 0)
        {
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;
            return;
        }

        _initialDeviceProbeFramesRemaining--;
        InputGlyphContext initialContext = ResolveInitialContext();
        if (ShouldPromoteInitialContext(initialContext))
        {
            SetCurrentContext(initialContext);
        }

        if (!InputGlyphProfileIds.IsKeyboardMouse(CurrentProfileId) || _initialDeviceProbeFramesRemaining <= 0)
        {
            _initialDeviceProbeFramesRemaining = 0;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;
        }
    }

    private static void OnAnyInputPerformed(InputAction.CallbackContext context)
    {
        InputControl control = context.control;
        if (!IsRelevantControl(control))
        {
            return;
        }

        InputDevice device = control.device;
        if (IsInitialProbeActive() && !IsGamepadLike(device))
        {
            return;
        }

        if (device == null || device.deviceId == CurrentDeviceId)
        {
            return;
        }

        InputGlyphContext deviceContext = BuildContext(device);
        if (deviceContext.DeviceId == CurrentDeviceId)
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (InputGlyphStringUtility.EqualsOrdinal(deviceContext.ProfileId, CurrentProfileId)
            && now - _lastSwitchTime < SameProfileDebounceWindow)
        {
            return;
        }

        _lastSwitchTime = now;
        SetCurrentContext(deviceContext);
    }

    private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (device == null)
        {
            return;
        }

        switch (change)
        {
            case InputDeviceChange.Removed:
            case InputDeviceChange.Disconnected:
                RemoveCachedContext(device.deviceId);
                if (device.deviceId == CurrentDeviceId)
                {
                    PromoteFallbackDevice(device.deviceId);
                }

                break;
            case InputDeviceChange.Reconnected:
            case InputDeviceChange.Added:
                RemoveCachedContext(device.deviceId);
                if (IsRelevantDevice(device) && ShouldPromoteAddedDevice(device))
                {
                    SetCurrentContext(BuildContext(device));
                }

                break;
        }
    }

    private static bool ShouldPromoteAddedDevice(InputDevice device)
    {
        if (device == null)
        {
            return false;
        }

        if (CurrentDeviceId < 0)
        {
            return true;
        }

        return IsGamepadLike(device) && InputGlyphProfileIds.IsKeyboardMouse(CurrentProfileId);
    }

    private static bool ShouldPromoteInitialContext(InputGlyphContext context)
    {
        if (context == null)
        {
            return false;
        }

        if (context.DeviceId < 0)
        {
            return false;
        }

        if (!InputGlyphProfileIds.IsKeyboardMouse(context.ProfileId) && InputGlyphProfileIds.IsKeyboardMouse(CurrentProfileId))
        {
            return true;
        }

        return CurrentDeviceId < 0 && InputGlyphProfileIds.IsKeyboardMouse(context.ProfileId);
    }

    private static bool IsInitialProbeActive()
    {
        return _initialDeviceProbeFramesRemaining > 0;
    }

    private static void PromoteFallbackDevice(int removedDeviceId)
    {
        for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
        {
            InputDevice device = InputSystem.devices[i];
            if (device == null || device.deviceId == removedDeviceId || !device.added || !IsRelevantDevice(device))
            {
                continue;
            }

            SetCurrentContext(BuildContext(device));
            return;
        }

        SetCurrentContext(CreateDefaultContext());
    }

    private static void SetCurrentContext(InputGlyphContext context, bool forceEmit = false)
    {
        InputGlyphContext resolvedContext = context != null ? context : CreateDefaultContext();
        InputGlyphContext currentContext = CurrentContext;
        if (!forceEmit && currentContext != null && currentContext.Equals(resolvedContext))
        {
            return;
        }

        bool profileChanged = !InputGlyphStringUtility.EqualsOrdinal(CurrentProfileId, resolvedContext.ProfileId);
        ApplyContext(resolvedContext, true);

        InputGlyphContext lastEmittedContext = _lastEmittedContext;
        if (forceEmit || lastEmittedContext == null || !lastEmittedContext.Equals(resolvedContext))
        {
            OnDeviceContextChanged?.Invoke(resolvedContext);
            if (profileChanged)
            {
                OnProfileChanged?.Invoke(resolvedContext.ProfileId);
            }

            _lastEmittedContext = resolvedContext;
        }
    }

    private static void ApplyContext(InputGlyphContext context, bool log)
    {
        InputGlyphContext resolvedContext = context != null ? context : CreateDefaultContext();
        CurrentContext = resolvedContext;
        CurrentProfileId = resolvedContext.ProfileId;
        CurrentDeviceId = resolvedContext.DeviceId;
        CurrentVendorId = resolvedContext.VendorId;
        CurrentProductId = resolvedContext.ProductId;
        CurrentDeviceName = resolvedContext.DeviceName;

#if UNITY_EDITOR
        if (log)
        {
            Log.Info(
                "Input device -> {0} name={1} vid=0x{2:X} pid=0x{3:X} id={4}",
                CurrentProfileId,
                CurrentDeviceName,
                CurrentVendorId,
                CurrentProductId,
                CurrentDeviceId);
        }
#endif
    }

    private static bool TryGetCachedContext(int deviceId, out InputGlyphContext context)
    {
        for (int i = 0; i < ContextCacheCount; i++)
        {
            if (ContextCache[i].DeviceId == deviceId)
            {
                context = ContextCache[i];
                return true;
            }
        }

        context = default;
        return false;
    }

    private static void AddCachedContext(InputGlyphContext context)
    {
        if (ContextCacheCount == ContextCache.Length)
        {
            Array.Resize(ref ContextCache, ContextCache.Length << 1);
        }

        ContextCache[ContextCacheCount] = context;
        ContextCacheCount++;
    }

    private static void RemoveCachedContext(int deviceId)
    {
        for (int i = 0; i < ContextCacheCount; i++)
        {
            if (ContextCache[i].DeviceId != deviceId)
            {
                continue;
            }

            ContextCacheCount--;
            if (i < ContextCacheCount)
            {
                ContextCache[i] = ContextCache[ContextCacheCount];
            }

            ContextCache[ContextCacheCount] = default;
            return;
        }
    }

    private static InputGlyphContext CreateDefaultContext()
    {
        return CreateKeyboardMouseContext(KeyboardScheme);
    }

    private static InputGlyphContext CreateKeyboardMouseContext(string controlScheme)
    {
        string resolvedScheme = string.IsNullOrWhiteSpace(controlScheme) ? KeyboardScheme : controlScheme;
        string profileId = InputGlyphService.ResolveProfileId(
            0,
            0,
            resolvedScheme,
            DefaultKeyboardDeviceName,
            DefaultKeyboardLayout,
            string.Empty,
            string.Empty,
            string.Empty);

        return new InputGlyphContext(
            -1,
            0,
            0,
            profileId,
            resolvedScheme,
            DefaultKeyboardDeviceName,
            DefaultKeyboardLayout,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    private static InputGlyphContext ResolveInitialContext()
    {
        InputDevice device;
        if (TryFindInitialGamepadLikeDevice(out device))
        {
            return BuildContext(device);
        }

        if (Keyboard.current != null && Keyboard.current.added)
        {
            return BuildContext(Keyboard.current);
        }

        if (TryFindInitialKeyboardDevice(out device))
        {
            return BuildContext(device);
        }

        if (Mouse.current != null && Mouse.current.added)
        {
            return BuildContext(Mouse.current);
        }

        if (TryFindInitialMouseDevice(out device))
        {
            return BuildContext(device);
        }

        return CreateDefaultContext();
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

    private static bool TryFindInitialKeyboardDevice(out InputDevice device)
    {
        for (int i = 0; i < InputSystem.devices.Count; i++)
        {
            InputDevice candidate = InputSystem.devices[i];
            if (candidate != null && candidate.added && candidate is Keyboard)
            {
                device = candidate;
                return true;
            }
        }

        device = null;
        return false;
    }

    private static bool TryFindInitialMouseDevice(out InputDevice device)
    {
        for (int i = 0; i < InputSystem.devices.Count; i++)
        {
            InputDevice candidate = InputSystem.devices[i];
            if (candidate != null && candidate.added && candidate is Mouse)
            {
                device = candidate;
                return true;
            }
        }

        device = null;
        return false;
    }

    private static bool IsRelevantDevice(InputDevice device)
    {
        return IsKeyboardMouseDevice(device) || IsGamepadLike(device);
    }

    private static bool IsKeyboardMouseDevice(InputDevice device)
    {
        return device is Keyboard || device is Mouse;
    }

    private static bool IsRelevantControl(InputControl control)
    {
        if (control == null || control.device == null || !IsRelevantDevice(control.device) || control.synthetic)
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

        if (device is Gamepad || device is Joystick)
        {
            return true;
        }

        string layout = device.layout;
        if (InputGlyphStringUtility.ContainsIgnoreCase(layout, "Mouse")
            || InputGlyphStringUtility.ContainsIgnoreCase(layout, "Touch")
            || InputGlyphStringUtility.ContainsIgnoreCase(layout, "Pen"))
        {
            return false;
        }

        return InputGlyphStringUtility.ContainsIgnoreCase(layout, "Gamepad")
               || InputGlyphStringUtility.ContainsIgnoreCase(layout, "Controller")
               || InputGlyphStringUtility.ContainsIgnoreCase(layout, "Joystick");
    }

    private static string ResolveControlScheme(InputDevice device)
    {
        return IsKeyboardMouseDevice(device) ? KeyboardScheme : GamepadScheme;
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
}
#endif
