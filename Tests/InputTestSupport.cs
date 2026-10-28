#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace AlicizaX.Input.Tests
{
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
    }

    /// <summary>
    /// 输入相关测试基类：隔离 Watch/Rebind 静态状态并管理测试设备。
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

        protected InputActionAsset Asset { get; private set; }
        protected Keyboard TestKeyboard { get; set; }
        protected Gamepad TestGamepad { get; set; }

        [SetUp]
        public void BaseSetUp()
        {
            EnableEditModePlayerUpdates();

            UXInput.Watch.Shutdown(true);
            UXInput.Rebind.Shutdown(true);
            UXInput.Reader.ResetForPlayMode();
            UXHotkeySystem.ResetForPlayMode();
            UXInput.Glyph.SetDatabase(null);
            Asset = InputTestAssets.Create();
            ConfigureAsset(Asset);
            UXInput.Watch.Initialize();
        }

        [TearDown]
        public void BaseTearDown()
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                if (_devices[i] != null && _devices[i].added)
                {
                    InputSystem.RemoveDevice(_devices[i]);
                }
            }

            _devices.Clear();
            UXInput.Glyph.SetDatabase(null);
            UXHotkeySystem.Shutdown();
            UXInput.Rebind.Shutdown(true);
            UXInput.Watch.Shutdown(true);
            InputTestAssets.Destroy(Asset);
            Asset = null;
            TestKeyboard = null;
            TestGamepad = null;

            RestoreEditModePlayerUpdates();
        }

        /// <summary>
        /// 派生测试可在启用资产前追加绑定。默认直接启用资产。
        /// </summary>
        protected virtual void ConfigureAsset(InputActionAsset asset)
        {
            asset.Enable();
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

        /// <summary>
        /// 用真实 InputActionProvider 注册测试资产，覆盖按名称解析动作的路径。
        /// </summary>
        protected void InitializeProvider()
        {
            typeof(InputActionProvider)
                .GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { null, Asset, null });
        }

        protected void ShutdownProvider()
        {
            typeof(InputActionProvider)
                .GetMethod("Shutdown", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { null });
        }
    }
}
#endif
