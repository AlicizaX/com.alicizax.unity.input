#if INPUTSYSTEM_SUPPORT
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Input
{
    [CustomEditor(typeof(HotkeyComponentBase), true)]
    public class HotkeyComponentEditor : UnityEditor.Editor
    {
        private static readonly GUIContent SubmitTargetLabel = new GUIContent("Submit Target");
        private static readonly GUIContent HolderLabel = new GUIContent("Holder");
        private static readonly GUIContent InputActionLabel = new GUIContent("Input Action");
        private static readonly GUIContent PressTypeLabel = new GUIContent("Press Type");
        private SerializedProperty _hotkeyAction;
        private SerializedProperty _hotkeyPressType;
        private SerializedProperty _submitTarget;
        private SerializedProperty _holder;

        private void OnEnable()
        {
            _submitTarget = serializedObject.FindProperty("_submitTarget");
            _holder = serializedObject.FindProperty("_holder");
            _hotkeyAction = serializedObject.FindProperty("_hotkeyAction");
            _hotkeyPressType = serializedObject.FindProperty("_hotkeyPressType");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            HotkeyComponentBase hotkeyComponent = (HotkeyComponentBase)target;

            EditorGUILayout.HelpBox(
                "Hotkeys auto-register to the nearest UIHolderObjectBase and observe the referenced action. The action owner controls when input is enabled.",
                MessageType.Info
            );

            if (_holder.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "No UIHolderObjectBase was found in parents. This hotkey will not register at runtime.",
                    MessageType.Warning
                );
            }

            if (hotkeyComponent is HotkeyComponent && _submitTarget.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No submit target was found on this object.", MessageType.Error);
            }

            if (_hotkeyAction.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Input Action is required. This hotkey will not register at runtime.", MessageType.Error);
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Hotkey Setting", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            if (_submitTarget != null)
            {
                EditorGUILayout.PropertyField(_submitTarget, SubmitTargetLabel);
            }

            EditorGUILayout.PropertyField(_holder, HolderLabel);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.PropertyField(_hotkeyAction, InputActionLabel);
            EditorGUILayout.PropertyField(_hotkeyPressType, PressTypeLabel);
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}

#endif
