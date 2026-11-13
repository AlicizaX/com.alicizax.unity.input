#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class GlyphBindingSelectionTests : InputDeviceTestBase
    {
        private InputAction _attack;

        protected override void ConfigureAsset(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap("Combat");
            _attack = map.AddAction("Attack", InputActionType.Button);
            _attack.AddBinding("<Keyboard>/f", groups: "KeyboardMouse");
            _attack.AddBinding("<Gamepad>/rightTrigger", groups: "Gamepad");
            asset.Enable();
        }

        [Test]
        public void BindingSelectionFollowsCurrentProfile()
        {
            AddGamepad();
            Assert.That(UXInput.Glyph.GetBindingControlPath(_attack), Is.EqualTo("<Gamepad>/rightTrigger"));
            Assert.That(UXInput.Glyph.GetDisplayName(_attack), Is.Not.Empty);

            SetKeys(Key.A);
            Assert.That(UXInput.Glyph.GetBindingControlPath(_attack), Is.EqualTo("<Keyboard>/f"));
            Assert.That(UXInput.Glyph.GetDisplayName(_attack), Is.Not.Empty);
        }

        [Test]
        public void TryGetBindingReportsSelectedBinding()
        {
            AddGamepad();

            Assert.That(UXInput.Glyph.TryGetBinding(_attack, null, out InputBinding binding), Is.True);
            Assert.That(binding.path, Is.EqualTo("<Gamepad>/rightTrigger"));
        }

        [Test]
        public void MissingProfileFallsBackToFirstBinding()
        {
            var glyph = UXInput.Glyph;
            UXInput.Shutdown();
            Assert.That(
                glyph.GetBindingControlPath(_attack),
                Is.EqualTo("<Keyboard>/f"),
                "the platform default profile must select the keyboard binding");
        }
    }
}
#endif
