using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class InputFeedbackObserverEditorTests
    {
        private GameObject _first;
        private GameObject _second;
        private InputFeedbackProfile _profile;
        private UnityEditor.Editor _editor;
        private EditorWindow _window;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<InputFeedbackProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null) _window.Close();
            Object.DestroyImmediate(_editor);
            Object.DestroyImmediate(_first);
            Object.DestroyImmediate(_second);
            Object.DestroyImmediate(_profile);
        }

        private static InputFeedbackObserver Create(out GameObject root, InputFeedbackMode mode)
        {
            root = new GameObject("feedback-observer");
            var observer = root.AddComponent<InputFeedbackObserver>();
            var serialized = new SerializedObject(observer);
            serialized.FindProperty("_mode").intValue = (int)mode;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return observer;
        }

        private static VisualElement ProfileRow(VisualElement root) => root.Q(InputFeedbackObserverEditor.ProfileRowName);

        private static string Hint(VisualElement root) => root.Q<Label>(InputFeedbackObserverEditor.HintName).text;

        [TestCase(InputFeedbackMode.Global, DisplayStyle.None, "全局主题")]
        [TestCase(InputFeedbackMode.Custom, DisplayStyle.Flex, "本组件的主题")]
        public void ProfileFieldOnlyShownInCustomMode(InputFeedbackMode mode, DisplayStyle display, string hint)
        {
            _editor = UnityEditor.Editor.CreateEditor(Create(out _first, mode));
            VisualElement root = _editor.CreateInspectorGUI();

            Assert.That(ProfileRow(root).style.display.value, Is.EqualTo(display));
            Assert.That(Hint(root), Does.Contain(hint));
        }

        [Test]
        public void MixedModesHideProfileAndSaySo()
        {
            InputFeedbackObserver global = Create(out _first, InputFeedbackMode.Global);
            InputFeedbackObserver custom = Create(out _second, InputFeedbackMode.Custom);
            _editor = UnityEditor.Editor.CreateEditor(new Object[] { global, custom });
            VisualElement root = _editor.CreateInspectorGUI();

            Assert.That(ProfileRow(root).style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(Hint(root), Does.Contain("不一致"));
        }

        [Test]
        public void SwitchingToGlobalClearsProfileReference()
        {
            var serialized = new SerializedObject(Create(out _first, InputFeedbackMode.Custom));
            serialized.FindProperty("_profile").objectReferenceValue = _profile;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();
            Assert.That(serialized.FindProperty("_profile").objectReferenceValue, Is.SameAs(_profile), "Custom 保留主题");

            serialized.FindProperty("_mode").intValue = (int)InputFeedbackMode.Global;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();
            Assert.That(serialized.FindProperty("_profile").objectReferenceValue, Is.Null,
                "Global 不读本组件主题，残留引用会成为看不见的资源依赖");
        }

        [UnityTest]
        public IEnumerator SwitchingModeInInspectorTogglesProfileField()
        {
            InputFeedbackObserver observer = Create(out _first, InputFeedbackMode.Global);
            _editor = UnityEditor.Editor.CreateEditor(observer);
            VisualElement root = _editor.CreateInspectorGUI();
            root.Bind(_editor.serializedObject);
            _window = ScriptableObject.CreateInstance<EditorWindow>();
            _window.rootVisualElement.Add(root);
            _window.Show();
            yield return null;
            Assert.That(ProfileRow(root).style.display.value, Is.EqualTo(DisplayStyle.None));

            _editor.serializedObject.FindProperty("_mode").intValue = (int)InputFeedbackMode.Custom;
            _editor.serializedObject.ApplyModifiedProperties();
            for (int frame = 0; frame < 10 && ProfileRow(root).style.display.value == DisplayStyle.None; frame++)
                yield return null;

            Assert.That(ProfileRow(root).style.display.value, Is.EqualTo(DisplayStyle.Flex), "切到 Custom 后显示主题字段");
            Assert.That(Hint(root), Does.Contain("本组件的主题"));
        }
    }
}
