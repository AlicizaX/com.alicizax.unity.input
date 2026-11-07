#if INPUTSYSTEM_SUPPORT
using System;
using NUnit.Framework;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class InputServiceTests : InputDeviceTestBase
    {
        [Test]
        public void ShutdownClearsLookup()
        {
            UXInput.Shutdown();
            Assert.That(UXInput.Reader.ResolveAction("Gameplay/Jump"), Is.Null);
            Assert.That(UXInput.Reader.TryResolveAction("Gameplay/Jump", out _), Is.False);
            Assert.That(UXInput.Reader.FindActionMap("Gameplay"), Is.Null);
        }

        [Test]
        public void InitializeBuildsFullPathLookupAndShutdownClearsIt()
        {

            Assert.That(UXInput.Reader.Actions, Is.SameAs(Asset));
            Assert.That(UXInput.Reader.FindActionMap("Gameplay"), Is.Not.Null);
            Assert.That(UXInput.Reader.TryResolveAction("Gameplay/Jump", out var resolved), Is.True);
            Assert.That(resolved, Is.SameAs(Action("Jump")));
            Assert.That(
                UXInput.Reader.ResolveAction("Jump"),
                Is.Null,
                "only MapName/ActionName full paths are registered");
            Assert.That(UXInput.Reader.ResolveAction("Gameplay/Missing"), Is.Null);

            UXInput.Shutdown();

            Assert.That(UXInput.Reader.Actions, Is.Null);
            Assert.That(UXInput.Reader.FindActionMap("Gameplay"), Is.Null);
            Assert.That(UXInput.Reader.TryResolveAction("Gameplay/Jump", out _), Is.False);
        }

        [Test]
        public void FindActionMapThrowsWhenRequestedAndAssetIsMissing()
        {
            UXInput.Shutdown();
            Assert.Throws<InvalidOperationException>(() => UXInput.Reader.FindActionMap("Gameplay", true));
        }
    }
}
#endif
