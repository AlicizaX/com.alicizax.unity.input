#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class InputActionProviderTests : InputDeviceTestBase
    {
        [SetUp]
        public void ResetProvider()
        {
            InputActionProvider.ResetForPlayMode();
        }

        [TearDown]
        public void ShutdownProviderAfterTest()
        {
            ShutdownProvider();
            UXInput.Watch.Shutdown(true);
        }

        [Test]
        public void LookupIsEmptyBeforeInitialize()
        {
            Assert.That(InputActionProvider.ResolveAction("Gameplay/Jump"), Is.Null);
            Assert.That(InputActionProvider.TryResolveAction("Gameplay/Jump", out _), Is.False);
            Assert.That(InputActionProvider.FindActionMap("Gameplay"), Is.Null);
        }

        [Test]
        public void InitializeBuildsFullPathLookupAndShutdownClearsIt()
        {
            InitializeProvider();

            Assert.That(InputActionProvider.Actions, Is.SameAs(Asset));
            Assert.That(InputActionProvider.FindActionMap("Gameplay"), Is.Not.Null);
            Assert.That(InputActionProvider.TryResolveAction("Gameplay/Jump", out var resolved), Is.True);
            Assert.That(resolved, Is.SameAs(Action("Jump")));
            Assert.That(
                InputActionProvider.ResolveAction("Jump"),
                Is.Null,
                "only MapName/ActionName full paths are registered");
            Assert.That(InputActionProvider.ResolveAction("Gameplay/Missing"), Is.Null);

            ShutdownProvider();

            Assert.That(InputActionProvider.Actions, Is.Null);
            Assert.That(InputActionProvider.FindActionMap("Gameplay"), Is.Null);
            Assert.That(InputActionProvider.TryResolveAction("Gameplay/Jump", out _), Is.False);
        }
    }
}
#endif
