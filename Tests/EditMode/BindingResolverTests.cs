#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class BindingResolverTests : InputDeviceTestBase
    {
        private InputAction _fire;
        private InputAction _move;

        protected override void ConfigureAsset(InputActionAsset asset)
        {
            InputActionMap map = asset.AddActionMap("Bindings");
            _fire = map.AddAction("Fire", InputActionType.Button);
            _fire.AddBinding("<Keyboard>/f", groups: "KeyboardMouse");
            _fire.AddBinding("<Gamepad>/rightTrigger", groups: "Gamepad;Xbox");

            _move = asset.FindAction("Gameplay/Move");
            asset.Enable();
        }

        [Test]
        public void RebindSelectionHonorsBindingGroup()
        {
            Assert.That(
                InputBindingResolver.SelectForRebind(_fire, null, "KeyboardMouse", null),
                Is.EqualTo(0));

            Assert.That(
                InputBindingResolver.SelectForRebind(_fire, null, "Gamepad", null),
                Is.EqualTo(1));

            Assert.That(
                InputBindingResolver.SelectForRebind(_fire, null, "Xbox", null),
                Is.EqualTo(1),
                "multi-token groups must match a single token");

            Assert.That(
                InputBindingResolver.SelectForRebind(_fire, null, "UnmatchedGroup", null),
                Is.EqualTo(-1),
                "an explicit target must not select an unrelated binding");
        }

        [Test]
        public void RebindSelectionHonorsRequiredControlPath()
        {
            Assert.That(
                InputBindingResolver.SelectForRebind(_fire, null, null, "<Gamepad>"),
                Is.EqualTo(1));

            Assert.That(
                InputBindingResolver.SelectForRebind(_fire, null, null, "<Keyboard>"),
                Is.EqualTo(0));

            Assert.That(InputBindingResolver.SelectForRebind(_fire, null, null, "<Touchscreen>"), Is.EqualTo(-1));
        }

        [Test]
        public void DisplaySelectionPrefersCurrentProfile()
        {
            InputProfileCatalog.TryGet(InputWatch.InputProfile.KeyboardMouse, out InputProfileInfo keyboardInfo);
            InputProfileCatalog.TryGet(InputWatch.InputProfile.Xbox, out InputProfileInfo xboxInfo);

            Assert.That(
                InputBindingResolver.SelectForDisplay(
                    _fire,
                    null,
                    InputWatch.InputProfile.KeyboardMouse,
                    keyboardInfo.BindingGroupHints),
                Is.EqualTo(0));

            Assert.That(
                InputBindingResolver.SelectForDisplay(
                    _fire,
                    null,
                    InputWatch.InputProfile.Xbox,
                    xboxInfo.BindingGroupHints),
                Is.EqualTo(1));
        }

        [Test]
        public void CompositePartSelectionReturnsNamedPart()
        {
            InputProfileCatalog.TryGet(InputWatch.InputProfile.KeyboardMouse, out InputProfileInfo info);

            int rebindIndex = InputBindingResolver.SelectForRebind(_move, "Up", null, "<Keyboard>");
            Assert.That(rebindIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(_move.bindings[rebindIndex].name, Is.EqualTo("Up"));

            int displayIndex = InputBindingResolver.SelectForDisplay(
                _move,
                "Down",
                InputWatch.InputProfile.KeyboardMouse,
                info.BindingGroupHints);
            Assert.That(displayIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(_move.bindings[displayIndex].name, Is.EqualTo("Down"));
        }

        [Test]
        public void CompositePartBindingIndexResolvesNamedPart()
        {
            AddKeyboard();

            Assert.That(InputBindingResolver.FindCompositePartBindingIndex(_move, "Up"), Is.EqualTo(1));
            Assert.That(InputBindingResolver.FindCompositePartBindingIndex(_move, "Missing"), Is.EqualTo(-1));
            Assert.That(InputBindingResolver.FindCompositePartBindingIndex(null, "Up"), Is.EqualTo(-1));
        }
    }
}
#endif
