#if INPUTSYSTEM_SUPPORT
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class GlyphComponentTests : InputDeviceTestBase
    {
        private GlyphDatabaseBuilder _builder;

        [TearDown]
        public void TearDownDatabase()
        {
            UXInput.Glyph.SetDatabase(null);
            _builder?.Dispose();
            _builder = null;
        }

        [Test]
        public void ImageShowsEntrySpriteThenPlaceholderThenClears()
        {
            AddKeyboard();
            _builder = new GlyphDatabaseBuilder();
            Sprite space = _builder.CreateSprite("SpaceButton");
            Sprite placeholder = _builder.CreateSprite("Placeholder");
            InputGlyphDatabase database = _builder.Build(
                placeholder,
                new GlyphProfileSpec
                {
                    Id = "KeyboardMouse",
                    Entries = new[]
                    {
                        new GlyphEntrySpec { ControlPaths = new[] { "<Keyboard>/space" }, Sprite = space }
                    }
                });
            UXInput.Glyph.SetDatabase(database);

            var gameObject = new GameObject("glyph-image", typeof(RectTransform), typeof(Image));
            try
            {
                var glyphImage = gameObject.AddComponent<InputGlyphImage>();
                typeof(InputGlyphImage)
                    .GetField("targetImage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(glyphImage, gameObject.GetComponent<Image>());

                glyphImage.SetAction(Action("Jump"));
                Assert.That(gameObject.GetComponent<Image>().sprite, Is.SameAs(space));

                glyphImage.SetAction(Action("Submit"));
                Assert.That(gameObject.GetComponent<Image>().sprite, Is.SameAs(placeholder));

                glyphImage.SetAction(null);
                Assert.That(gameObject.GetComponent<Image>().sprite, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EmptyTemplateTextKeepsFollowingBindingChanges()
        {
            AddKeyboard();
            var gameObject = new GameObject("glyph-text-empty-template", typeof(RectTransform), typeof(TextMeshProUGUI));
            try
            {
                var text = gameObject.GetComponent<TextMeshProUGUI>();
                text.text = string.Empty;
                var glyphText = gameObject.AddComponent<InputGlyphText>();
                typeof(InputGlyphText)
                    .GetField("targetText", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(glyphText, text);

                InputAction jump = Action("Jump");
                glyphText.SetAction(jump);
                string before = text.text;
                Assert.That(before, Is.Not.Empty);

                UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/enter");
                UXInput.Rebind.ConfirmApply(false, false);

                Assert.That(text.text, Is.Not.EqualTo(before),
                    "an empty template must stay empty; the rendered token must not become the next template");
                Assert.That(text.text, Is.EqualTo(UXInput.Glyph.GetDisplayName(UXInput.Glyph.GetBindingControlPath(jump))));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ImageClearsSpriteWhenNoPlaceholderIsConfigured()
        {
            AddKeyboard();
            _builder = new GlyphDatabaseBuilder();
            InputGlyphDatabase database = _builder.Build(null, new GlyphProfileSpec { Id = "KeyboardMouse" });
            UXInput.Glyph.SetDatabase(database);

            var gameObject = new GameObject("glyph-image-empty", typeof(RectTransform), typeof(Image));
            try
            {
                var image = gameObject.GetComponent<Image>();
                image.sprite = _builder.CreateSprite("Preset");
                var glyphImage = gameObject.AddComponent<InputGlyphImage>();
                typeof(InputGlyphImage)
                    .GetField("targetImage", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(glyphImage, image);

                glyphImage.SetAction(Action("Jump"));
                Assert.That(image.sprite, Is.Null, "missing glyph without placeholder clears the sprite");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
#endif
