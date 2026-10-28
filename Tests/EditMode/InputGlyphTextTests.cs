#if INPUTSYSTEM_SUPPORT
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class InputGlyphTextTests
    {
        [Test]
        public void TemplateReplacementHandlesMissingAndInvalidPlaceholders()
        {
            MethodInfo replace = typeof(InputGlyphText).GetMethod(
                "ReplaceFirstPlaceholder",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(replace, Is.Not.Null);

            Assert.That(
                replace.Invoke(null, new object[] { "Jump {0}", "<sprite name=\"A\">" }),
                Is.EqualTo("Jump <sprite name=\"A\">"));
            Assert.That(
                replace.Invoke(null, new object[] { "Jump {1}", "X" }),
                Is.EqualTo("Jump {1}"),
                "unsupported placeholders must not throw and must stay untouched");
            Assert.That(
                replace.Invoke(null, new object[] { "Jump", "X" }),
                Is.EqualTo("Jump"));
            Assert.That(
                replace.Invoke(null, new object[] { string.Empty, "X" }),
                Is.EqualTo("X"),
                "an empty template renders the token itself");
            Assert.That(
                replace.Invoke(null, new object[] { "{0}{0}", "A" }),
                Is.EqualTo("A{0}"),
                "only the first placeholder is replaced");
        }

        [Test]
        public void ComponentRefreshesWithoutTargetAndDoesNotThrow()
        {
            var gameObject = new GameObject("glyph-text-test", typeof(RectTransform));
            try
            {
                var glyphText = gameObject.AddComponent<InputGlyphText>();
                MethodInfo refresh = typeof(InputGlyphText).GetMethod(
                    "RefreshGlyph",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(refresh, Is.Not.Null);
                Assert.DoesNotThrow(() => refresh.Invoke(glyphText, null));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
#endif
