using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class InputFeedbackInspectorTests
    {
        private Object _target;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
        }

        [Test]
        public void ConflictingDeviceMasksCanBeIdentifiedAndRepaired()
        {
            _target = ScriptableObject.CreateInstance<InputFeedbackProfile>();

            var serialized = new SerializedObject(_target);
            SerializedProperty rules = serialized.FindProperty("_rules");
            rules.arraySize = 4;
            SetRule(rules, 0, InputFeedbackCue.Press, InputFeedbackDevice.KeyboardMouse);
            SetRule(rules, 1, InputFeedbackCue.Press, InputFeedbackDevice.KeyboardMouse | InputFeedbackDevice.Gamepad);
            SetRule(rules, 2, InputFeedbackCue.FocusEnter, InputFeedbackDevice.Gamepad);
            SetRule(rules, 3, InputFeedbackCue.Press, InputFeedbackDevice.Gamepad);

            Assert.That(InputFeedbackRuleEditor.AvailableDevices(rules, 1), Is.EqualTo(InputFeedbackDevice.Touch));
            Assert.That(InputFeedbackRuleEditor.HasConflict(rules, 1), Is.True);
            Assert.That(InputFeedbackRuleEditor.HasConflict(rules, 2), Is.False);

            InputFeedbackRuleEditor.RepairOverlaps(rules);
            Assert.That(rules.arraySize, Is.EqualTo(3));
            Assert.That(rules.GetArrayElementAtIndex(0).FindPropertyRelative("Devices").intValue,
                Is.EqualTo((int)InputFeedbackDevice.KeyboardMouse));
            Assert.That(rules.GetArrayElementAtIndex(1).FindPropertyRelative("Devices").intValue,
                Is.EqualTo((int)InputFeedbackDevice.Gamepad));
            Assert.That(InputFeedbackRuleEditor.HasConflict(rules, 1), Is.False);
        }

        private static void SetRule(SerializedProperty rules, int index, InputFeedbackCue cue, InputFeedbackDevice devices)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);
            rule.FindPropertyRelative("Cue").enumValueIndex = (int)cue;
            rule.FindPropertyRelative("Devices").intValue = (int)devices;
        }
    }
}
