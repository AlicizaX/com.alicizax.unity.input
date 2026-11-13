#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Input
{
    [CustomEditor(typeof(InputFeedbackProfile))]
    internal sealed class InputFeedbackProfileEditor : UnityEditor.Editor
    {
        private SerializedProperty _rules;

        private void OnEnable()
        {
            _rules = serializedObject.FindProperty("_rules");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InputFeedbackRuleEditor.Draw(_rules);
            serializedObject.ApplyModifiedProperties();
        }
    }

    internal static class InputFeedbackRuleEditor
    {
        private static readonly InputFeedbackDevice[] DeviceValues =
        {
            InputFeedbackDevice.Touch,
            InputFeedbackDevice.KeyboardMouse,
            InputFeedbackDevice.Gamepad,
        };

        private static readonly string[] DeviceNames = { "触屏", "键鼠", "手柄" };

        internal static void Draw(SerializedProperty rules)
        {
            for (int i = 0; i < rules.arraySize; i++)
            {
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                SerializedProperty cue = rule.FindPropertyRelative("Cue");
                SerializedProperty devices = rule.FindPropertyRelative("Devices");

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"规则 {i + 1}", EditorStyles.boldLabel);
                if (GUILayout.Button("删除", GUILayout.Width(50)))
                {
                    rules.DeleteArrayElementAtIndex(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(cue, new GUIContent("事件"));
                if (EditorGUI.EndChangeCheck())
                {
                    int available = (int)AvailableDevices(rules, i);
                    devices.intValue &= available;
                    if (devices.intValue == 0)
                        devices.intValue = available & -available;
                }

                DrawDevices(rules, i, devices);

                if (cue.enumValueIndex == (int)InputFeedbackCue.FocusEnter)
                    EditorGUILayout.PropertyField(rule.FindPropertyRelative("AllowProgrammaticFocus"), new GUIContent("允许程序化焦点发声"));

                SerializedProperty clip = rule.FindPropertyRelative("Clip");
                EditorGUILayout.PropertyField(clip, new GUIContent("音频"));
                if (clip.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("未指定音频；匹配此规则时静音。", MessageType.Info);

                if (devices.intValue == 0)
                    EditorGUILayout.HelpBox("请选择至少一种设备。", MessageType.Warning);

                if (HasConflict(rules, i))
                    EditorGUILayout.HelpBox("与其他规则的事件和设备重叠；运行时采用列表首条。", MessageType.Warning);

                EditorGUILayout.EndVertical();
            }

            if (HasAnyConflict(rules) && GUILayout.Button("修复重叠规则"))
                RepairOverlaps(rules);

            if (TryFindAvailableCue(rules, out int nextCue, out int availableDevices))
            {
                if (GUILayout.Button("添加规则"))
                {
                    int index = rules.arraySize++;
                    SerializedProperty rule = rules.GetArrayElementAtIndex(index);
                    rule.FindPropertyRelative("Cue").enumValueIndex = nextCue;
                    rule.FindPropertyRelative("Devices").intValue = availableDevices;
                    rule.FindPropertyRelative("AllowProgrammaticFocus").boolValue = false;
                    rule.FindPropertyRelative("Clip").objectReferenceValue = null;
                }
            }
        }

        private static void DrawDevices(SerializedProperty rules, int index, SerializedProperty devices)
        {
            int available = (int)AvailableDevices(rules, index);
            var names = new List<string>(4);
            var bits = new List<int>(4);
            int selected = 0;

            for (int i = 0; i < DeviceValues.Length; i++)
            {
                int bit = (int)DeviceValues[i];
                if ((available & bit) == 0)
                    continue;

                int option = names.Count;
                names.Add(DeviceNames[i]);
                bits.Add(bit);
                if ((devices.intValue & bit) != 0)
                    selected |= 1 << option;
            }

            if (names.Count == 0)
            {
                EditorGUILayout.LabelField("设备", "没有可用设备");
                return;
            }

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.MaskField("设备", selected, names.ToArray());
            if (!EditorGUI.EndChangeCheck())
                return;

            int mask = 0;
            for (int i = 0; i < bits.Count; i++)
            {
                if ((next & (1 << i)) != 0)
                    mask |= bits[i];
            }

            devices.intValue = mask;
        }

        internal static InputFeedbackDevice AvailableDevices(SerializedProperty rules, int index)
        {
            int cue = rules.GetArrayElementAtIndex(index).FindPropertyRelative("Cue").enumValueIndex;
            int used = 0;
            for (int i = 0; i < rules.arraySize; i++)
            {
                if (i == index)
                    continue;

                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                if (rule.FindPropertyRelative("Cue").enumValueIndex == cue)
                    used |= rule.FindPropertyRelative("Devices").intValue;
            }

            return (InputFeedbackDevice)((int)InputFeedbackDevice.All & ~used);
        }

        internal static bool HasConflict(SerializedProperty rules, int index)
        {
            int devices = rules.GetArrayElementAtIndex(index).FindPropertyRelative("Devices").intValue;
            return (devices & ~(int)AvailableDevices(rules, index)) != 0;
        }

        private static bool HasAnyConflict(SerializedProperty rules)
        {
            for (int i = 0; i < rules.arraySize; i++)
            {
                if (HasConflict(rules, i))
                    return true;
            }

            return false;
        }

        internal static void RepairOverlaps(SerializedProperty rules)
        {
            for (int i = 0; i < rules.arraySize; i++)
            {
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                SerializedProperty devices = rule.FindPropertyRelative("Devices");
                int cue = rule.FindPropertyRelative("Cue").enumValueIndex;
                int used = 0;
                for (int j = 0; j < i; j++)
                {
                    SerializedProperty earlier = rules.GetArrayElementAtIndex(j);
                    if (earlier.FindPropertyRelative("Cue").enumValueIndex == cue)
                        used |= earlier.FindPropertyRelative("Devices").intValue;
                }

                devices.intValue &= ~used;
                if (devices.intValue == 0)
                {
                    rules.DeleteArrayElementAtIndex(i);
                    i--;
                }
            }
        }

        private static bool TryFindAvailableCue(SerializedProperty rules, out int cue, out int devices)
        {
            for (cue = 0; cue < Enum.GetValues(typeof(InputFeedbackCue)).Length; cue++)
            {
                devices = (int)InputFeedbackDevice.All;
                for (int i = 0; i < rules.arraySize; i++)
                {
                    SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                    if (rule.FindPropertyRelative("Cue").enumValueIndex == cue)
                        devices &= ~rule.FindPropertyRelative("Devices").intValue;
                }

                if (devices != 0)
                    return true;
            }

            devices = 0;
            return false;
        }
    }
}
#endif
