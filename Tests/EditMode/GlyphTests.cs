#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class GlyphTests : InputDeviceTestBase
    {
        private GlyphDatabaseBuilder _builder;
        private InputGlyphDatabase _database;

        [TearDown]
        public void TearDownDatabase()
        {
            UXInput.Glyph.SetDatabase(null);
            _builder?.Dispose();
            _builder = null;
            _database = null;
        }

        [Test]
        public void DatabaseFlattensFallbackChainAtBuildTime()
        {
            _builder = new GlyphDatabaseBuilder();
            Sprite south = _builder.CreateSprite("SouthButton");
            _database = _builder.Build(
                null,
                new GlyphProfileSpec
                {
                    Id = "Xbox",
                    Entries = new[]
                    {
                        new GlyphEntrySpec { ControlPaths = new[] { "<Gamepad>/buttonSouth" }, Sprite = south }
                    }
                },
                new GlyphProfileSpec
                {
                    Id = "SteamController",
                    Fallbacks = new[] { "Xbox" }
                },
                new GlyphProfileSpec { Id = "GenericGamepad" });

            Assert.That(_database.TryGetSprite("Xbox", "gamepad/buttonsouth", out Sprite direct), Is.True);
            Assert.That(direct, Is.SameAs(south));

            Assert.That(
                _database.TryGetSprite("SteamController", "gamepad/buttonsouth", out Sprite viaFallback),
                Is.True,
                "a profile must resolve entries through its fallback chain");
            Assert.That(viaFallback, Is.SameAs(south));

            Assert.That(_database.TryGetSprite("GenericGamepad", "gamepad/buttonsouth", out _), Is.False);
        }

        [Test]
        public void MissingGlyphDoesNotPretendToBeAHitButExposesPlaceholder()
        {
            _builder = new GlyphDatabaseBuilder();
            Sprite placeholder = _builder.CreateSprite("Placeholder");
            _database = _builder.Build(placeholder, new GlyphProfileSpec { Id = "Xbox" });

            Assert.That(_database.TryGetSprite("Xbox", "gamepad/buttonsouth", out Sprite sprite), Is.False);
            Assert.That(sprite, Is.Null);
            Assert.That(_database.Placeholder, Is.SameAs(placeholder));

            Assert.That(
                _database.TryGetGlyph("Xbox", "gamepad/buttonsouth", out _, out string tmpName),
                Is.False);
            Assert.That(tmpName, Is.Null);
        }

        [Test]
        public void BindingGroupHintsComeFromDatabaseConfiguration()
        {
            _builder = new GlyphDatabaseBuilder();
            _database = _builder.Build(
                null,
                new GlyphProfileSpec { Id = "Xbox", Hints = new[] { "xbox", "pad" } });

            Assert.That(_database.TryGetBindingGroupHints("Xbox", out string[] hints), Is.True);
            Assert.That(hints, Is.EqualTo(new[] { "xbox", "pad" }));
            Assert.That(_database.TryGetBindingGroupHints("UnknownProfile", out _), Is.False);
        }

        [Test]
        public void GlyphTextQueryFallsBackToReadableNameInsteadOfPlaceholderSprite()
        {
            AddKeyboard();
            _builder = new GlyphDatabaseBuilder();
            Sprite placeholder = _builder.CreateSprite("Placeholder");
            _database = _builder.Build(placeholder, new GlyphProfileSpec { Id = "KeyboardMouse" });
            UXInput.Glyph.SetDatabase(_database);

            Assert.That(
                UXInput.Glyph.TryGetTag("<Keyboard>/space", out string tag, out string fallback),
                Is.False);
            Assert.That(tag, Is.Null);
            Assert.That(fallback, Is.EqualTo("Space"));

            Assert.That(UXInput.Glyph.TryGetSprite("<Keyboard>/space", out Sprite sprite), Is.True);
            Assert.That(sprite, Is.SameAs(placeholder), "sprite queries may use the placeholder");
        }

        [Test]
        public void GlyphEntryResolvesToTmpTag()
        {
            AddGamepad();
            _builder = new GlyphDatabaseBuilder();
            Sprite south = _builder.CreateSprite("SouthButton");
            _database = _builder.Build(
                null,
                new GlyphProfileSpec
                {
                    Id = "GenericGamepad",
                    Entries = new[]
                    {
                        new GlyphEntrySpec { ControlPaths = new[] { "<Gamepad>/buttonSouth" }, Sprite = south }
                    }
                });
            UXInput.Glyph.SetDatabase(_database);

            Assert.That(
                UXInput.Glyph.TryGetTag("<Gamepad>/buttonSouth", out string tag, out _),
                Is.True);
            Assert.That(tag, Is.EqualTo("<sprite name=\"SouthButton\">"));
        }

        [Test]
        public void GlyphKeyNormalizationIsStable()
        {
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath("<Gamepad>/buttonSouth"), Is.EqualTo("gamepad/buttonsouth"));
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath("<Keyboard>/LeftShift"), Is.EqualTo("keyboard/leftshift"));
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath("<Mouse>/leftButton"), Is.EqualTo("mouse/leftbutton"));
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath("<XInputController>/buttonSouth"), Is.EqualTo("gamepad/buttonsouth"));
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath(string.Empty), Is.Empty);
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath("not-a-path"), Is.Empty);
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath("<>/space"), Is.Empty);
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath("<Gamepad>/"), Is.Empty);
        }

        [Test]
        public void DisplayNameAndProfileIdQueriesAreStable()
        {
            AddKeyboard();
            string firstProfileId = UXInput.Watch.CurrentProfileId;
            string secondProfileId = UXInput.Watch.CurrentProfileId;
            Assert.That(secondProfileId, Is.SameAs(firstProfileId), "profile id is a cached string");

            string firstDisplay = UXInput.Glyph.GetDisplayName("<Keyboard>/space");
            string secondDisplay = UXInput.Glyph.GetDisplayName("<Keyboard>/space");
            Assert.That(secondDisplay, Is.SameAs(firstDisplay), "display names are cached");

            string firstKey = UXInput.Glyph.GetGlyphKeyFromControlPath("<Keyboard>/space");
            string secondKey = UXInput.Glyph.GetGlyphKeyFromControlPath("<Keyboard>/space");
            Assert.That(secondKey, Is.SameAs(firstKey), "glyph keys are cached");
        }

        [Test]
        public void ActionDisplayNameUsesCachedBinding()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");

            string first = UXInput.Glyph.GetDisplayName(jump);
            string second = UXInput.Glyph.GetDisplayName(jump);
            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void FallbackCyclesTerminateAndReturnNoEntry()
        {
            _builder = new GlyphDatabaseBuilder();
            _database = _builder.Build(
                null,
                new GlyphProfileSpec { Id = "A", Fallbacks = new[] { "B" } },
                new GlyphProfileSpec { Id = "B", Fallbacks = new[] { "A" } });

            Assert.That(_database.TryGetSprite("A", "gamepad/buttonsouth", out _), Is.False);
            Assert.That(_database.TryGetSprite("B", "gamepad/buttonsouth", out _), Is.False);
        }

        [Test]
        public void NearestProfileEntryWinsOverFallback()
        {
            _builder = new GlyphDatabaseBuilder();
            Sprite near = _builder.CreateSprite("NearButton");
            Sprite far = _builder.CreateSprite("FarButton");
            _database = _builder.Build(
                null,
                new GlyphProfileSpec
                {
                    Id = "Specific",
                    Fallbacks = new[] { "Fallback" },
                    Entries = new[]
                    {
                        new GlyphEntrySpec { ControlPaths = new[] { "<Gamepad>/buttonSouth" }, Sprite = near }
                    }
                },
                new GlyphProfileSpec
                {
                    Id = "Fallback",
                    Entries = new[]
                    {
                        new GlyphEntrySpec { ControlPaths = new[] { "<Gamepad>/buttonSouth" }, Sprite = far }
                    }
                });

            Assert.That(_database.TryGetSprite("Specific", "gamepad/buttonsouth", out Sprite specific), Is.True);
            Assert.That(specific, Is.SameAs(near));
            Assert.That(_database.TryGetSprite("Fallback", "gamepad/buttonsouth", out Sprite fallback), Is.True);
            Assert.That(fallback, Is.SameAs(far));
        }

        [Test]
        public void DatabaseLookupIsCaseInsensitive()
        {
            _builder = new GlyphDatabaseBuilder();
            Sprite south = _builder.CreateSprite("SouthButton");
            _database = _builder.Build(
                null,
                new GlyphProfileSpec
                {
                    Id = "Xbox",
                    Entries = new[]
                    {
                        new GlyphEntrySpec { ControlPaths = new[] { "<Gamepad>/buttonSouth" }, Sprite = south }
                    }
                });

            Assert.That(_database.TryGetSprite("xbox", "GAMEPAD/BUTTONSOUTH", out Sprite sprite), Is.True);
            Assert.That(sprite, Is.SameAs(south));
        }

        [Test]
        public void PlaceholderIsOnlyReturnedWhenConfigured()
        {
            _builder = new GlyphDatabaseBuilder();
            _database = _builder.Build(null, new GlyphProfileSpec { Id = "KeyboardMouse" });
            UXInput.Glyph.SetDatabase(_database);

            Assert.That(
                UXInput.Glyph.TryGetSprite("<Keyboard>/space", out Sprite sprite),
                Is.False);
            Assert.That(sprite, Is.Null);
            Assert.That(_database.Placeholder, Is.Null);
        }

        [Test]
        public void NullAndEmptyGlyphQueriesAreSafe()
        {
            Assert.That(UXInput.Glyph.GetBindingControlPath(null), Is.Empty);
            Assert.That(UXInput.Glyph.GetDisplayName((InputAction)null), Is.Empty);
            Assert.That(UXInput.Glyph.GetGlyphKeyFromControlPath(null), Is.Empty);
            Assert.That(
                UXInput.Glyph.TryGetSprite((string)null, out Sprite sprite),
                Is.False);
            Assert.That(sprite, Is.Null);
            Assert.That(
                UXInput.Glyph.TryGetTag((InputAction)null, null, out string tag, out string fallback),
                Is.False);
            Assert.That(tag, Is.Null);
            Assert.That(fallback, Is.Empty);
        }

        [Test]
        public void ActionReferenceOverloadsMatchActionOverloads()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            _builder = new GlyphDatabaseBuilder();
            Sprite space = _builder.CreateSprite("SpaceButton");
            _database = _builder.Build(
                null,
                new GlyphProfileSpec
                {
                    Id = "KeyboardMouse",
                    Entries = new[]
                    {
                        new GlyphEntrySpec { ControlPaths = new[] { "<Keyboard>/space" }, Sprite = space }
                    }
                });
            UXInput.Glyph.SetDatabase(_database);

            InputActionReference reference = InputActionReference.Create(jump);
            try
            {
                Assert.That(
                    UXInput.Glyph.TryGetSprite(reference, null, out Sprite fromReference),
                    Is.True);
                Assert.That(
                    UXInput.Glyph.TryGetSprite(jump, null, out Sprite fromAction),
                    Is.True);
                Assert.That(fromReference, Is.SameAs(fromAction));
                Assert.That(UXInput.Glyph.GetBindingControlPath(reference), Is.EqualTo("<Keyboard>/space"));
            }
            finally
            {
                Object.DestroyImmediate(reference);
            }
        }
    }
}
#endif
