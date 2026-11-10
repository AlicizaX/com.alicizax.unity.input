#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace AlicizaX.Input.Tests
{
    /// <summary>为输入测试绑定逻辑视图；框架生命周期本身由 UI 测试验证。</summary>
    public static class InputTestViews
    {
        private sealed class TestHolder : UIHolderObjectBase { }
        private sealed class TestWindow : UIWindow<UIHolderObjectBase> { }
        private sealed class TestWidget : UIWidget<UIHolderObjectBase> { }
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo OwnerField = typeof(UIHolderObjectBase).GetField("_owner", Fields);
        private static readonly FieldInfo HolderField = typeof(UIBase).GetField("_holder", Fields);
        private static readonly FieldInfo StateField = typeof(UIBase).GetField("_state", Fields);
        private static readonly FieldInfo ParentField = typeof(UIWidget).GetField("Parent", Fields);
        private static readonly List<UIHolderObjectBase> Holders = new();

        public static UIBase Bind(GameObject root, UIBase parent = null, UIState state = UIState.Opened) =>
            Bind(root.AddComponent<TestHolder>(), parent, state);

        public static UIBase Bind(UIHolderObjectBase holder, UIBase parent = null, UIState state = UIState.Opened)
        {
            UIBase view = parent == null ? new TestWindow() : new TestWidget();
            if (parent != null) ParentField.SetValue(view, parent);
            HolderField.SetValue(view, holder);
            StateField.SetValue(view, state);
            OwnerField.SetValue(holder, view);
            Holders.Add(holder);
            return view;
        }

        public static void SetState(UIBase view, UIState state) => StateField.SetValue(view, state);

        public static void SetState(UIHolderObjectBase holder, UIState state) =>
            SetState((UIBase)OwnerField.GetValue(holder), state);

        public static UIHolderObjectBase GetHolder(this UIBase view) => (UIHolderObjectBase)HolderField.GetValue(view);

        public static void Raise(UIHolderObjectBase holder, string eventName) =>
            ((Action)typeof(UIHolderObjectBase).GetField(eventName, Fields).GetValue(holder))?.Invoke();

        public static void UnbindAll()
        {
            foreach (UIHolderObjectBase holder in Holders) OwnerField.SetValue(holder, null);
            Holders.Clear();
        }

        public static void Unbind(UIHolderObjectBase holder)
        {
            OwnerField.SetValue(holder, null);
            Holders.Remove(holder);
        }
    }

    /// <summary>
    /// 测试共用的输入资源工厂。所有资产都在测试内创建，不依赖工程资源。
    /// </summary>
    public static class InputTestAssets
    {
        public static InputActionAsset Create()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "InputTests";

            InputActionMap gameplay = asset.AddActionMap("Gameplay");
            gameplay.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            gameplay.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            gameplay.AddAction("Secondary", InputActionType.Button, "<Keyboard>/space");

            InputAction move = gameplay.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            return asset;
        }

        public static void Destroy(UnityEngine.Object value)
        {
            if (value != null)
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        /// <summary>
        /// 移除运行时创建的测试设备，只保留编辑器原生设备。测试之间共享同一个 InputSystem，
        /// 不清理会把上一次会话的设备（尤其是手柄）带进下一次初始化，改变默认输入 Profile。
        /// </summary>
        public static void RemoveTestDevices()
        {
            for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = InputSystem.devices[i];
                if (string.IsNullOrEmpty(device.description.deviceClass))
                {
                    InputSystem.RemoveDevice(device);
                }
            }
        }
    }

    /// <summary>
    /// 输入相关测试基类：通过门面启停独立模块实例并管理测试设备。
    /// </summary>
    public abstract class InputDeviceTestBase
    {
        private readonly List<InputDevice> _devices = new List<InputDevice>();

#if UNITY_EDITOR
        private static readonly FieldInfo InputManagerField =
            typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic);

        private object _inputManager;
        private PropertyInfo _runPlayerUpdatesInEditMode;
        private bool _previousRunPlayerUpdatesInEditMode;
#endif

        protected string BindingFilePath { get; private set; }
        protected InputActionAsset Asset { get; private set; }
        protected Keyboard TestKeyboard { get; set; }
        protected Gamepad TestGamepad { get; set; }

        [SetUp]
        public void BaseSetUp()
        {
            EnableEditModePlayerUpdates();

            InputTestAssets.RemoveTestDevices();
            Assert.That(UXInput.IsInitialized, Is.False, "the previous test must close its input session");
            BindingFilePath = Path.Combine(Path.GetTempPath(), "AlicizaXInputTests", Guid.NewGuid().ToString("N"), "input_bindings.json");
            Asset = InputTestAssets.Create();
            ConfigureAsset(Asset);
            UXInput.Initialize(Asset, null, BindingFilePath);
        }

        [TearDown]
        public void BaseTearDown()
        {
            UXInput.Shutdown();
            for (int i = 0; i < _devices.Count; i++)
            {
                if (_devices[i] != null && _devices[i].added)
                {
                    InputSystem.RemoveDevice(_devices[i]);
                }
            }

            _devices.Clear();
            InputTestAssets.Destroy(Asset);
            Asset = null;
            TestKeyboard = null;
            TestGamepad = null;

            string directory = Path.GetDirectoryName(BindingFilePath);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            RestoreEditModePlayerUpdates();
        }

        /// <summary>
        /// 派生测试可在启用资产前追加绑定。初始化门面之前配置资产。
        /// </summary>
        protected virtual void ConfigureAsset(InputActionAsset asset)
        {
        }

        private void EnableEditModePlayerUpdates()
        {
#if UNITY_EDITOR
            if (InputManagerField == null)
            {
                return;
            }

            _inputManager = InputManagerField.GetValue(null);
            _runPlayerUpdatesInEditMode = _inputManager?.GetType().GetProperty(
                "runPlayerUpdatesInEditMode",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_runPlayerUpdatesInEditMode == null || !_runPlayerUpdatesInEditMode.CanWrite)
            {
                return;
            }

            _previousRunPlayerUpdatesInEditMode = (bool)_runPlayerUpdatesInEditMode.GetValue(_inputManager);
            _runPlayerUpdatesInEditMode.SetValue(_inputManager, true);
#endif
        }

        private void RestoreEditModePlayerUpdates()
        {
#if UNITY_EDITOR
            if (_runPlayerUpdatesInEditMode != null && _inputManager != null)
            {
                _runPlayerUpdatesInEditMode.SetValue(_inputManager, _previousRunPlayerUpdatesInEditMode);
            }

            _inputManager = null;
            _runPlayerUpdatesInEditMode = null;
#endif
        }

        protected Keyboard AddKeyboard()
        {
            if (TestKeyboard == null)
            {
                TestKeyboard = Track(InputSystem.AddDevice<Keyboard>("TestKeyboard"));
            }

            return TestKeyboard;
        }

        protected Gamepad AddGamepad()
        {
            if (TestGamepad == null)
            {
                TestGamepad = Track(InputSystem.AddDevice<Gamepad>("TestGamepad"));
            }

            return TestGamepad;
        }

        protected Keyboard AddExtraKeyboard(string name)
        {
            return Track(InputSystem.AddDevice<Keyboard>(name));
        }

        protected TDevice Track<TDevice>(TDevice device) where TDevice : InputDevice
        {
            if (device != null)
            {
                _devices.Add(device);
            }

            return device;
        }

        protected void SetKeys(params Key[] keys)
        {
            Keyboard keyboard = AddKeyboard();
            InputSystem.QueueStateEvent(keyboard, keys.Length == 0 ? new KeyboardState() : new KeyboardState(keys));
            InputSystem.Update();
        }

        protected InputAction Action(string actionName)
        {
            return Asset.FindAction("Gameplay/" + actionName);
        }

    }
}
#endif
