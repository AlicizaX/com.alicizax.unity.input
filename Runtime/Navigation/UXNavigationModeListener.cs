#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;

namespace AlicizaX.UI.UXNavigation
{
    [DisallowMultipleComponent]
    public sealed class UXNavigationModeListener : MonoBehaviour
    {
        private const float StickThresholdSqr = 0.04f;
        private const float AxisThreshold = 0.2f;
        private const int InitialDeviceProbeFrames = 30;

        private const string MouseActionName = "UXMouseInput";
        private const string TouchActionName = "UXTouchInput";
        private const string KeyboardActionName = "UXKeyboardInput";
        private const string GamepadActionName = "UXGamepadInput";
        private const string MouseDeltaBinding = "<Mouse>/delta";
        private const string MouseScrollBinding = "<Mouse>/scroll";
        private const string MouseLeftButtonBinding = "<Mouse>/leftButton";
        private const string MouseRightButtonBinding = "<Mouse>/rightButton";
        private const string MouseMiddleButtonBinding = "<Mouse>/middleButton";
        private const string KeyboardAnyKeyBinding = "<Keyboard>/anyKey";
        private const string GamepadButtonSouthBinding = "<Gamepad>/buttonSouth";
        private const string GamepadButtonNorthBinding = "<Gamepad>/buttonNorth";
        private const string GamepadButtonEastBinding = "<Gamepad>/buttonEast";
        private const string GamepadButtonWestBinding = "<Gamepad>/buttonWest";
        private const string GamepadStartButtonBinding = "<Gamepad>/startButton";
        private const string GamepadSelectButtonBinding = "<Gamepad>/selectButton";
        private const string GamepadLeftShoulderBinding = "<Gamepad>/leftShoulder";
        private const string GamepadRightShoulderBinding = "<Gamepad>/rightShoulder";
        private const string GamepadDpadBinding = "<Gamepad>/dpad";
        private const string GamepadLeftStickBinding = "<Gamepad>/leftStick";
        private const string GamepadRightStickBinding = "<Gamepad>/rightStick";
        private const string TouchPressBinding = "<Touchscreen>/primaryTouch/press";
        private const string TouchDeltaBinding = "<Touchscreen>/primaryTouch/delta";


        private InputAction _mouseAction;
        private InputAction _touchAction;
        private InputAction _keyboardAction;
        private InputAction _gamepadAction;
        private int _initialDeviceProbeFramesRemaining;

        public static UXInputMode CurrentMode { get; private set; } = UXInputMode.Keyboard;

        internal static bool RequiresSelectedForCurrentMode => _instance != null && _instance.IsRequireSelected(CurrentMode);

        public static event Action<UXInputMode> OnModeChanged;

        private static UXNavigationModeListener _instance;

        /// <summary>
        /// 必须保证存在可导航焦点
        /// </summary>
        public static bool GamepadRequireLowFocus = true;

        public static bool KeyBoardRequireLowFocus = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void EnsureInstance()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject("[UXNavigationModeListener]");
                go.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<UXNavigationModeListener>();
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            hideFlags = HideFlags.HideAndDontSave;
            CreateActions();
            SetMode(ResolveInitialMode());
        }


        private void OnDestroy()
        {
            DisposeActions();
            _instance = null;
        }

        private void OnEnable()
        {
            SetActionsEnabled(true);
            InputSystem.onDeviceChange += OnDeviceChange;
            _initialDeviceProbeFramesRemaining = InitialDeviceProbeFrames;
            InputSystem.onAfterUpdate += OnAfterInputUpdate;
        }

        private void OnDisable()
        {
            SetActionsEnabled(false);
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onAfterUpdate -= OnAfterInputUpdate;
        }


        private void OnAfterInputUpdate()
        {
            if (_initialDeviceProbeFramesRemaining <= 0)
            {
                InputSystem.onAfterUpdate -= OnAfterInputUpdate;
                return;
            }

            _initialDeviceProbeFramesRemaining--;
            if (CurrentMode != UXInputMode.Gamepad && HasGamepadLikeDevice())
            {
                SetMode(UXInputMode.Gamepad);
            }

            if (CurrentMode == UXInputMode.Gamepad || _initialDeviceProbeFramesRemaining <= 0)
            {
                _initialDeviceProbeFramesRemaining = 0;
                InputSystem.onAfterUpdate -= OnAfterInputUpdate;
            }
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
                    if (IsGamepadLike(device) && CurrentMode != UXInputMode.Gamepad)
                    {
                        SetMode(UXInputMode.Gamepad);
                    }

                    break;
                case InputDeviceChange.Removed:
                case InputDeviceChange.Disconnected:
                    if ((CurrentMode == UXInputMode.Gamepad && !HasGamepadLikeDevice()) ||
                        (CurrentMode == UXInputMode.Keyboard && !HasKeyboardOrMouseDevice()) ||
                        (CurrentMode == UXInputMode.Touch && !HasTouchDevice()))
                    {
                        SetMode(ResolveInitialMode());
                    }

                    break;
            }
        }

        private void CreateActions()
        {
            if (_mouseAction != null)
            {
                return;
            }

            _mouseAction = new InputAction(MouseActionName, InputActionType.PassThrough);
            _mouseAction.AddBinding(MouseDeltaBinding);
            _mouseAction.AddBinding(MouseScrollBinding);
            _mouseAction.AddBinding(MouseLeftButtonBinding);
            _mouseAction.AddBinding(MouseRightButtonBinding);
            _mouseAction.AddBinding(MouseMiddleButtonBinding);
            _mouseAction.performed += OnMouseInput;

            _touchAction = new InputAction(TouchActionName, InputActionType.PassThrough);
            _touchAction.AddBinding(TouchPressBinding);
            _touchAction.AddBinding(TouchDeltaBinding);
            _touchAction.performed += OnTouchInput;

            _keyboardAction = new InputAction(KeyboardActionName, InputActionType.PassThrough);
            _keyboardAction.AddBinding(KeyboardAnyKeyBinding);
            _keyboardAction.performed += OnKeyboardInput;

            _gamepadAction = new InputAction(GamepadActionName, InputActionType.PassThrough);
            _gamepadAction.AddBinding(GamepadButtonSouthBinding);
            _gamepadAction.AddBinding(GamepadButtonNorthBinding);
            _gamepadAction.AddBinding(GamepadButtonEastBinding);
            _gamepadAction.AddBinding(GamepadButtonWestBinding);
            _gamepadAction.AddBinding(GamepadStartButtonBinding);
            _gamepadAction.AddBinding(GamepadSelectButtonBinding);
            _gamepadAction.AddBinding(GamepadLeftShoulderBinding);
            _gamepadAction.AddBinding(GamepadRightShoulderBinding);
            _gamepadAction.AddBinding(GamepadDpadBinding);
            _gamepadAction.AddBinding(GamepadLeftStickBinding);
            _gamepadAction.AddBinding(GamepadRightStickBinding);
            _gamepadAction.performed += OnGamepadInput;
        }

        private void SetActionsEnabled(bool enabled)
        {
            if (_mouseAction == null)
            {
                return;
            }

            if (enabled)
            {
                _mouseAction.Enable();
                _touchAction.Enable();
                _keyboardAction.Enable();
                _gamepadAction.Enable();
                return;
            }

            _mouseAction.Disable();
            _touchAction.Disable();
            _keyboardAction.Disable();
            _gamepadAction.Disable();
        }

        private void DisposeActions()
        {
            if (_mouseAction != null)
            {
                _mouseAction.performed -= OnMouseInput;
                _mouseAction.Dispose();
                _mouseAction = null;
            }

            if (_touchAction != null)
            {
                _touchAction.performed -= OnTouchInput;
                _touchAction.Dispose();
                _touchAction = null;
            }

            if (_keyboardAction != null)
            {
                _keyboardAction.performed -= OnKeyboardInput;
                _keyboardAction.Dispose();
                _keyboardAction = null;
            }

            if (_gamepadAction != null)
            {
                _gamepadAction.performed -= OnGamepadInput;
                _gamepadAction.Dispose();
                _gamepadAction = null;
            }
        }

        private static void OnMouseInput(InputAction.CallbackContext context)
        {
            if (CanKeyboardSwitchMode() && IsInputMeaningful(context.control))
            {
                SetMode(UXInputMode.Keyboard);
            }
        }

        private static void OnTouchInput(InputAction.CallbackContext context)
        {
            if (CanTouchSwitchMode() && IsInputMeaningful(context.control))
            {
                SetMode(UXInputMode.Touch);
            }
        }

        private static void OnKeyboardInput(InputAction.CallbackContext context)
        {
            if (CanKeyboardSwitchMode() && IsInputMeaningful(context.control))
            {
                SetMode(UXInputMode.Keyboard);
            }
        }

        private static void OnGamepadInput(InputAction.CallbackContext context)
        {
            if (IsInputMeaningful(context.control))
            {
                SetMode(UXInputMode.Gamepad);
            }
        }

        private static bool IsInputMeaningful(InputControl control)
        {
            if (control == null || control.device == null || control.synthetic)
            {
                return false;
            }

            switch (control)
            {
                case ButtonControl button:
                    return button.IsPressed();
                case StickControl stick:
                    return stick.ReadValue().sqrMagnitude >= StickThresholdSqr;
                case Vector2Control vector2:
                    return vector2.ReadValue().sqrMagnitude >= StickThresholdSqr;
                case AxisControl axis:
                    return Mathf.Abs(axis.ReadValue()) >= AxisThreshold;
                default:
                    return !control.noisy;
            }
        }

        private static UXInputMode ResolveInitialMode()
        {
            if (HasGamepadLikeDevice() || IsFixedGamepadNavigationPlatform())
            {
                return UXInputMode.Gamepad;
            }

            if (CanKeyboardSwitchMode() && HasKeyboardOrMouseDevice())
            {
                return UXInputMode.Keyboard;
            }

            if (HasTouchDevice())
            {
                return UXInputMode.Touch;
            }

            return UXInputMode.Keyboard;
        }

        private static bool HasGamepadLikeDevice()
        {
            for (int i = 0; i < InputSystem.devices.Count; i++)
            {
                InputDevice device = InputSystem.devices[i];
                if (device != null && device.added && IsGamepadLike(device))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasKeyboardOrMouseDevice()
        {
            return (Keyboard.current != null && Keyboard.current.added) ||
                   (Mouse.current != null && Mouse.current.added);
        }

        private static bool HasTouchDevice()
        {
            return Touchscreen.current != null && Touchscreen.current.added;
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

            string layout = device.layout ?? string.Empty;
            return layout.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layout.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layout.IndexOf("Joystick", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool CanTouchSwitchMode()
        {
            return !IsFixedGamepadNavigationPlatform();
        }

        private static bool CanKeyboardSwitchMode()
        {
#if UNITY_SWITCH
            return false;
#else
            return true;
#endif
        }

        private static bool IsFixedGamepadNavigationPlatform()
        {
#if UNITY_SWITCH
            return true;
#else
            return false;
#endif
        }

        private bool IsRequireSelected(UXInputMode mode)
        {
            return (mode == UXInputMode.Gamepad && GamepadRequireLowFocus)
                   || (mode == UXInputMode.Keyboard && KeyBoardRequireLowFocus);
        }

        internal static void SetMode(UXInputMode mode)
        {
            if (CurrentMode == mode)
            {
                if (RequiresSelectedForCurrentMode)
                {
                    UXNavigationManager.RequestEnsureSelection();
                }

                return;
            }

            CurrentMode = mode;
            OnModeChanged?.Invoke(mode);
        }
    }
}
#endif
