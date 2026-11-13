#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class WatchTests : InputDeviceTestBase
    {
        // 通过 deviceClass 命中标准 Gamepad 布局，再用 product/厂商文本驱动 Profile 识别。
        private const string TestGamepadClass = "Gamepad";

        private sealed class ConfigurableGamepad : Gamepad
        {
            public void SetDisplayName(string value) { displayName = value; }
        }

        [Test]
        public void ConfigurationChangeInvalidatesOnlyItsDeviceAndCurrentFastPath()
        {
            InputSystem.RegisterLayout<ConfigurableGamepad>();
            var first = Track(InputSystem.AddDevice<ConfigurableGamepad>());
            var second = Track(InputSystem.AddDevice<ConfigurableGamepad>());
            try
            {
                UXInput.Watch.ResolveContext(first);
                var secondContext = UXInput.Watch.ResolveContext(second);
                first.SetDisplayName("Renamed first pad");
                second.SetDisplayName("Renamed second pad");
                InputSystem.QueueConfigChangeEvent(first);
                InputSystem.Update();
                Assert.That(UXInput.Watch.ResolveContext(first).DeviceName, Is.EqualTo("Renamed first pad"));
                Assert.That(UXInput.Watch.ResolveContext(second), Is.EqualTo(secondContext),
                    "an unrelated device keeps its cached context until its own notification");
                InputSystem.QueueStateEvent(first, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                Assert.That(UXInput.Watch.Current.DeviceName, Is.EqualTo("Renamed first pad"));
                InputSystem.RemoveDevice(first);
                InputSystem.AddDevice(first);
                InputSystem.QueueStateEvent(first, new GamepadState().WithButton(GamepadButton.East));
                InputSystem.Update();
                Assert.That(UXInput.Watch.Current.DeviceId, Is.EqualTo(first.deviceId));
                Assert.That(UXInput.Watch.Current.DeviceName, Is.EqualTo("Renamed first pad"));
            }
            finally
            {
                if (first.added) InputSystem.RemoveDevice(first);
                InputSystem.RemoveDevice(second);
                InputSystem.RemoveLayout(nameof(ConfigurableGamepad));
            }
        }

        [Test]
        public void SameDeviceActivityDoesNotRepeatContextEvents()
        {
            var pad = AddGamepad();
            int activity = 0;
            int contexts = 0;
            UXInput.Watch.OnInputActivity += _ => activity++;
            UXInput.Watch.OnContextChanged += _ => contexts++;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            InputSystem.Update();
            InputSystem.Update();
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East));
            InputSystem.Update();
            InputSystem.QueueStateEvent(pad, new GamepadState());
            InputSystem.Update();
            Assert.That(activity, Is.EqualTo(2));
            Assert.That(contexts, Is.Zero);
        }

        [Test]
        public void AlternatingDevicesEmitsOnlyChangedCategories()
        {
            var keyboard = AddKeyboard();
            var mouse = Track(InputSystem.AddDevice<Mouse>());
            var pad = AddGamepad();
            int contexts = 0;
            int types = 0;
            int profiles = 0;
            UXInput.Watch.OnContextChanged += _ => contexts++;
            UXInput.Watch.OnInputTypeChanged += _ => types++;
            UXInput.Watch.OnInputProfileChanged += _ => profiles++;
            SetKeys(Key.A);
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = Vector2.one * 10f });
            InputSystem.Update();
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            InputSystem.Update();
            Assert.That(contexts, Is.EqualTo(3));
            Assert.That(types, Is.EqualTo(2));
            Assert.That(profiles, Is.EqualTo(2));
            Assert.That(UXInput.Watch.Current.DeviceId, Is.EqualTo(pad.deviceId));
        }

        [Test]
        public void MouseJitterDoesNotLeaveGamepad()
        {
            var mouse = Track(InputSystem.AddDevice<Mouse>());
            var pad = AddGamepad();
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
            InputSystem.Update();
            Assert.That(UXInput.Watch.IsNavigationInput, Is.True);

            InputSystem.QueueStateEvent(mouse, new MouseState { delta = Vector2.right });
            InputSystem.Update();
            Assert.That(UXInput.Watch.IsNavigationInput, Is.True, "1px below the travel threshold");

            InputSystem.QueueStateEvent(pad, new GamepadState());
            InputSystem.Update();
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.East));
            InputSystem.Update();
            for (int i = 0; i < 3; i++)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { delta = Vector2.right });
                InputSystem.Update();
            }

            Assert.That(UXInput.Watch.IsNavigationInput, Is.True, "gamepad activity discards accumulated jitter");

            InputSystem.QueueStateEvent(mouse, new MouseState { delta = Vector2.right * 10f });
            InputSystem.Update();
            Assert.That(UXInput.Watch.IsKeyboardMouseInput, Is.True);
        }

        [Test]
        public void StickNoiseAndReleaseDoNotCountAsActivity()
        {
            var pad = AddGamepad();
            SetKeys(Key.A);
            int activity = 0;
            UXInput.Watch.OnInputActivity += _ => activity++;
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0.1f, 0f) });
            InputSystem.Update();
            Assert.That(activity, Is.Zero);
            Assert.That(UXInput.Watch.IsKeyboardMouseInput, Is.True);
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right });
            InputSystem.Update();
            Assert.That(activity, Is.EqualTo(1));
            Assert.That(UXInput.Watch.IsNavigationInput, Is.True);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            InputSystem.Update();
            Assert.That(activity, Is.EqualTo(1));
        }

        [Test]
        public void BuildContextClassifiesGamepad()
        {
            Gamepad gamepad = AddGamepad();
            InputWatch.InputContext context = UXInput.Watch.BuildContext(gamepad);

            Assert.That(context.InputType, Is.EqualTo(InputWatch.InputType.Gamepad));
            Assert.That(context.InputProfile, Is.EqualTo(InputWatch.InputProfile.GenericGamepad));
            Assert.That(context.DeviceId, Is.EqualTo(gamepad.deviceId));
            Assert.That(context.DeviceName, Is.Not.Empty);
            Assert.That(context.IsValid, Is.True);
        }

        [TestCase("Xbox Wireless Controller", InputWatch.InputProfile.Xbox)]
        [TestCase("DualSense Wireless Controller", InputWatch.InputProfile.PlayStation)]
        [TestCase("DualShock 4", InputWatch.InputProfile.PlayStation)]
        [TestCase("PlayStation Move", InputWatch.InputProfile.PlayStation)]
        [TestCase("Switch Pro Controller", InputWatch.InputProfile.Switch)]
        [TestCase("Steam Deck Controller", InputWatch.InputProfile.SteamDeck)]
        [TestCase("Steam Controller", InputWatch.InputProfile.SteamController)]
        [TestCase("Generic USB Pad", InputWatch.InputProfile.GenericGamepad)]
        public void BuildContextClassifiesGamepadProfileByDeviceText(
            string product,
            InputWatch.InputProfile expectedProfile)
        {
            var description = new InputDeviceDescription
            {
                deviceClass = TestGamepadClass,
                product = product,
                manufacturer = "AlicizaX Tests",
                interfaceName = "HID"
            };

            var gamepad = (Gamepad)InputSystem.AddDevice(description);
            Track(gamepad);
            Assert.That(gamepad, Is.Not.Null, "test gamepad layout must create a device");

            InputWatch.InputContext context = UXInput.Watch.BuildContext(gamepad);

            Assert.That(context.InputType, Is.EqualTo(InputWatch.InputType.Gamepad));
            Assert.That(context.InputProfile, Is.EqualTo(expectedProfile));
        }

        [Test]
        public void BuildContextClassifiesJoystickAsGamepadAndTouch()
        {
            Joystick joystick = Track(InputSystem.AddDevice<Joystick>("TestJoystick"));
            Assert.That(
                UXInput.Watch.BuildContext(joystick).InputType,
                Is.EqualTo(InputWatch.InputType.Gamepad));
            Assert.That(
                UXInput.Watch.BuildContext(joystick).InputProfile,
                Is.EqualTo(InputWatch.InputProfile.GenericGamepad));

            Touchscreen touchscreen = Track(InputSystem.AddDevice<Touchscreen>("TestTouchscreen"));
            Assert.That(
                UXInput.Watch.BuildContext(touchscreen).InputType,
                Is.EqualTo(InputWatch.InputType.Touch));
            Assert.That(
                UXInput.Watch.BuildContext(touchscreen).InputProfile,
                Is.EqualTo(InputWatch.InputProfile.Touch));
        }

        [Test]
        public void BuildContextWithNullDeviceReturnsPlatformDefault()
        {
            InputWatch.InputContext context = UXInput.Watch.BuildContext(null);

            Assert.That(context.InputType, Is.EqualTo(InputWatch.InputType.KeyboardMouse));
            Assert.That(context.InputProfile, Is.EqualTo(InputWatch.InputProfile.KeyboardMouse));
            Assert.That(context.IsValid, Is.True);
        }

        [Test]
        public void DeviceChangePromotesAndFallsBackContext()
        {
            Gamepad gamepad = AddGamepad();

            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(InputWatch.InputType.Gamepad));
            Assert.That(UXInput.Watch.CurrentProfileId, Is.EqualTo("GenericGamepad"));
            Assert.That(UXInput.Watch.IsNavigationInput, Is.True);

            InputSystem.RemoveDevice(gamepad);
            TestGamepad = null;

            Assert.That(
                UXInput.Watch.CurrentInputType,
                Is.EqualTo(InputWatch.InputType.KeyboardMouse),
                "removing the only gamepad must fall back to the platform default");
            Assert.That(UXInput.Watch.CurrentProfileId, Is.EqualTo("KeyboardMouse"));
        }

        [Test]
        public void ReAddingGamepadPromotesContextAgain()
        {
            Gamepad first = AddGamepad();
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(InputWatch.InputType.Gamepad));

            InputSystem.RemoveDevice(first);
            TestGamepad = null;
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(InputWatch.InputType.KeyboardMouse));

            AddGamepad();
            Assert.That(
                UXInput.Watch.CurrentInputType,
                Is.EqualTo(InputWatch.InputType.Gamepad),
                "the context cache must invalidate on device changes");
        }

        [Test]
        public void LastInputDeviceWinsContext()
        {
            AddKeyboard();
            AddGamepad();
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(InputWatch.InputType.Gamepad));

            SetKeys(Key.A);
            Assert.That(
                UXInput.Watch.CurrentInputType,
                Is.EqualTo(InputWatch.InputType.KeyboardMouse),
                "the most recent input device must own the context");
        }

        [Test]
        public void ContextCacheReturnsSameContextForSameDevice()
        {
            Gamepad gamepad = AddGamepad();
            InputWatch.InputContext first = UXInput.Watch.ResolveContext(gamepad);
            InputWatch.InputContext second = UXInput.Watch.ResolveContext(gamepad);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void ProfileIdLookupReturnsCachedString()
        {
            string first = UXInput.Watch.CurrentProfileId;
            string second = UXInput.Watch.CurrentProfileId;
            Assert.That(first, Is.EqualTo("KeyboardMouse"));
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void ShutdownResetsContextAndListeners()
        {
            AddGamepad();
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(InputWatch.InputType.Gamepad));

            var watch = UXInput.Watch;
            UXInput.Shutdown();

            Assert.That(UXInput.Watch, Is.Null);
            Assert.That(watch.CurrentInputType, Is.EqualTo(InputWatch.InputType.KeyboardMouse));
            Assert.That(watch.CurrentProfileId, Is.EqualTo("KeyboardMouse"));
        }

        [Test]
        public void BuiltContextsForSameDeviceAreEqual()
        {
            Gamepad gamepad = AddGamepad();
            InputWatch.InputContext first = UXInput.Watch.BuildContext(gamepad);
            InputWatch.InputContext second = UXInput.Watch.BuildContext(gamepad);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(second.GetHashCode(), Is.EqualTo(first.GetHashCode()));
        }
    }
}
#endif
