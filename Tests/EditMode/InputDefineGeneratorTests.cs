#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class InputDefineGeneratorTests
    {
        [Test]
        public void GeneratedCodeContainsOnlyStateFreeAccessors()
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                asset.name = "DefineTests";
                InputActionMap map = asset.AddActionMap("Gameplay");
                map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
                map.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick", expectedControlLayout: "Vector2");

                var options = new InputDefineCodeGenerator.Options
                {
                    ClassName = "TestInput",
                    NamespaceName = "Generated",
                    IncludeActionProperties = true
                };

                string code = InputDefineCodeGenerator.Generate(asset, options);

                Assert.That(code, Does.Contain("namespace Generated"));
                Assert.That(code, Does.Contain("public static class TestInput"));
                Assert.That(code, Does.Contain("public static class Gameplay"));
                Assert.That(code, Does.Contain("JumpPath"));
                Assert.That(code, Does.Contain("JumpAction => UXInput.Reader.ResolveAction(JumpPath)"));
                Assert.That(code, Does.Contain("IsButtonPressed(JumpPath)"));
                Assert.That(code, Does.Contain("ReadValue<Vector2>(MovePath)"));

                Assert.That(code, Does.Not.Contain("IsButtonPressedOnce"));
                Assert.That(code, Does.Not.Contain("IsPressedOnce"));
                Assert.That(code, Does.Not.Contain("Toggle"));
                Assert.That(code, Does.Not.Contain("ResetToggledButton"));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void SafeIdentifierHandlesKeywordsDigitsAndSeparators()
        {
            Assert.That(InputDefineCodeGenerator.ToSafeIdentifier("class", true), Is.EqualTo("Class"));
            Assert.That(InputDefineCodeGenerator.ToSafeIdentifier("1abc", true), Is.EqualTo("_1abc"));
            Assert.That(InputDefineCodeGenerator.ToSafeIdentifier("move dir", true), Is.EqualTo("MoveDir"));
            Assert.That(InputDefineCodeGenerator.ToSafeIdentifier(string.Empty, true), Is.Empty);
        }
    }
}
#endif
