#if INPUTSYSTEM_SUPPORT
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityEditor.UI
{
    [CustomEditor(typeof(HotkeyComponent), true)]
    public class HotkeyComponentEditor : Editor
    {
        private SerializedProperty _hotkeyAction;
        private SerializedProperty _hotkeyPressType;
        private SerializedProperty _hotkeyActionOwnershipMode;
        private SerializedProperty _hotkeyConsumesInput;
        private SerializedProperty _component;
        private SerializedProperty _holder;

        private void OnEnable()
        {
            _component = serializedObject.FindProperty("_component");
            _holder = serializedObject.FindProperty("_holder");
            _hotkeyAction = serializedObject.FindProperty("_hotkeyAction");
            _hotkeyPressType = serializedObject.FindProperty("_hotkeyPressType");
            _hotkeyActionOwnershipMode = serializedObject.FindProperty("_hotkeyActionOwnershipMode");
            _hotkeyConsumesInput = serializedObject.FindProperty("_hotkeyConsumesInput");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            HotkeyComponent hotkeyComponent = (HotkeyComponent)target;

            EditorGUILayout.HelpBox(
                "Hotkeys auto-register to the nearest UIHolderObjectBase at runtime.",
                MessageType.Info
            );

            if (_holder.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "No UIHolderObjectBase was found in parents. This hotkey will not register at runtime.",
                    MessageType.Warning
                );
            }

            if (_component.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No submit target was found on this object.", MessageType.Error);
                if (hotkeyComponent.TryGetComponent(typeof(ISubmitHandler), out Component submitHandler))
                {
                    _component.objectReferenceValue = submitHandler;
                }
            }
            else if (_component.objectReferenceValue is not ISubmitHandler)
            {
                EditorGUILayout.HelpBox("Submit target must implement ISubmitHandler. The invalid reference will be cleared.", MessageType.Error);
                _component.objectReferenceValue = null;
            }

            if (_hotkeyAction.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("Input Action is required. This hotkey will not register at runtime.", MessageType.Error);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Hotkey Setting", EditorStyles.boldLabel);

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(_component, new GUIContent("Component"));
                EditorGUILayout.PropertyField(_holder, new GUIContent("Holder"));
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.PropertyField(_hotkeyAction, new GUIContent("Input Action"));
                EditorGUILayout.PropertyField(_hotkeyPressType, new GUIContent("Press Type"));
                EditorGUILayout.PropertyField(_hotkeyActionOwnershipMode, new GUIContent("Action Ownership"));
                EditorGUILayout.PropertyField(_hotkeyConsumesInput, new GUIContent("Consumes Input"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}

#endif
