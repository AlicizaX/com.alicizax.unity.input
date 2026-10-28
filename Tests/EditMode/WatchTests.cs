#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class WatchTests : InputDeviceTestBase
    {
        // 通过 deviceClass 命中标准 Gamepad 布局，再用 product/厂商文本驱动 Profile 识别。
        private const string TestGamepadClass = "Gamepad";

        [Test]
        public void BuildContextClassifiesGamepad()
        {
            Gamepad gamepad = AddGamepad();
            UXInput.Watch.InputContext context = UXInput.Watch.BuildContext(gamepad);

            Assert.That(context.InputType, Is.EqualTo(UXInput.Watch.InputType.Gamepad));
            Assert.That(context.InputProfile, Is.EqualTo(UXInput.Watch.InputProfile.GenericGamepad));
            Assert.That(context.ControlScheme, Is.EqualTo("Gamepad"));
            Assert.That(context.DeviceId, Is.EqualTo(gamepad.deviceId));
            Assert.That(context.DeviceName, Is.Not.Empty);
            Assert.That(context.IsValid, Is.True);
        }

        [TestCase("Xbox Wireless Controller", UXInput.Watch.InputProfile.Xbox)]
        [TestCase("DualSense Wireless Controller", UXInput.Watch.InputProfile.PlayStation)]
        [TestCase("DualShock 4", UXInput.Watch.InputProfile.PlayStation)]
        [TestCase("PlayStation Move", UXInput.Watch.InputProfile.PlayStation)]
        [TestCase("Switch Pro Controller", UXInput.Watch.InputProfile.Switch)]
        [TestCase("Steam Deck Controller", UXInput.Watch.InputProfile.SteamDeck)]
        [TestCase("Steam Controller", UXInput.Watch.InputProfile.SteamController)]
        [TestCase("Generic USB Pad", UXInput.Watch.InputProfile.GenericGamepad)]
        public void BuildContextClassifiesGamepadProfileByDeviceText(
            string product,
            UXInput.Watch.InputProfile expectedProfile)
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

            UXInput.Watch.InputContext context = UXInput.Watch.BuildContext(gamepad);

            Assert.That(context.InputType, Is.EqualTo(UXInput.Watch.InputType.Gamepad));
            Assert.That(context.InputProfile, Is.EqualTo(expectedProfile));
        }

        [Test]
        public void BuildContextClassifiesJoystickAndTouch()
        {
            Joystick joystick = Track(InputSystem.AddDevice<Joystick>("TestJoystick"));
            Assert.That(
                UXInput.Watch.BuildContext(joystick).InputType,
                Is.EqualTo(UXInput.Watch.InputType.Joystick));
            Assert.That(
                UXInput.Watch.BuildContext(joystick).InputProfile,
                Is.EqualTo(UXInput.Watch.InputProfile.GenericJoystick));

            Touchscreen touchscreen = Track(InputSystem.AddDevice<Touchscreen>("TestTouchscreen"));
            Assert.That(
                UXInput.Watch.BuildContext(touchscreen).InputType,
                Is.EqualTo(UXInput.Watch.InputType.Touch));
            Assert.That(
                UXInput.Watch.BuildContext(touchscreen).InputProfile,
                Is.EqualTo(UXInput.Watch.InputProfile.Touch));
        }

        [Test]
        public void BuildContextWithNullDeviceReturnsPlatformDefault()
        {
            UXInput.Watch.InputContext context = UXInput.Watch.BuildContext(null);

            Assert.That(context.InputType, Is.EqualTo(UXInput.Watch.InputType.KeyboardMouse));
            Assert.That(context.InputProfile, Is.EqualTo(UXInput.Watch.InputProfile.KeyboardMouse));
            Assert.That(context.IsValid, Is.True);
        }

        [Test]
        public void DeviceChangePromotesAndFallsBackContext()
        {
            Gamepad gamepad = AddGamepad();

            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(UXInput.Watch.InputType.Gamepad));
            Assert.That(UXInput.Watch.CurrentProfileId, Is.EqualTo("GenericGamepad"));
            Assert.That(UXInput.Watch.IsNavigationInput, Is.True);

            InputSystem.RemoveDevice(gamepad);
            TestGamepad = null;

            Assert.That(
                UXInput.Watch.CurrentInputType,
                Is.EqualTo(UXInput.Watch.InputType.KeyboardMouse),
                "removing the only gamepad must fall back to the platform default");
            Assert.That(UXInput.Watch.CurrentProfileId, Is.EqualTo("KeyboardMouse"));
        }

        [Test]
        public void ReAddingGamepadPromotesContextAgain()
        {
            Gamepad first = AddGamepad();
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(UXInput.Watch.InputType.Gamepad));

            InputSystem.RemoveDevice(first);
            TestGamepad = null;
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(UXInput.Watch.InputType.KeyboardMouse));

            AddGamepad();
            Assert.That(
                UXInput.Watch.CurrentInputType,
                Is.EqualTo(UXInput.Watch.InputType.Gamepad),
                "the context cache must invalidate on device changes");
        }

        [Test]
        public void LastInputDeviceWinsContext()
        {
            AddKeyboard();
            AddGamepad();
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(UXInput.Watch.InputType.Gamepad));

            SetKeys(Key.A);
            Assert.That(
                UXInput.Watch.CurrentInputType,
                Is.EqualTo(UXInput.Watch.InputType.KeyboardMouse),
                "the most recent input device must own the context");
        }

        [Test]
        public void ContextCacheReturnsSameContextForSameDevice()
        {
            Gamepad gamepad = AddGamepad();
            UXInput.Watch.InputContext first = UXInput.Watch.ResolveContext(gamepad);
            UXInput.Watch.InputContext second = UXInput.Watch.ResolveContext(gamepad);

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
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(UXInput.Watch.InputType.Gamepad));

            UXInput.Watch.Shutdown(true);

            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(UXInput.Watch.InputType.KeyboardMouse));
            Assert.That(UXInput.Watch.CurrentProfileId, Is.EqualTo("KeyboardMouse"));
        }

        [Test]
        public void BuiltContextsForSameDeviceAreEqual()
        {
            Gamepad gamepad = AddGamepad();
            UXInput.Watch.InputContext first = UXInput.Watch.BuildContext(gamepad);
            UXInput.Watch.InputContext second = UXInput.Watch.BuildContext(gamepad);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(second.GetHashCode(), Is.EqualTo(first.GetHashCode()));
        }
    }
}
#endif
