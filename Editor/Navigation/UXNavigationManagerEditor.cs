#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX.Editor;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.UI.UXNavigation
{
    [CustomEditor(typeof(UXNavigationManager))]
    public sealed class UXNavigationManagerEditor : UnityEditor.Editor
    {
        private const string NoneOptionName = "None";
        private const float SectionToolbarHeight = 30f;
        private const float PropertyLabelWidth = 142f;

        private readonly List<string> _processorTypeNames = new List<string>();
        private string[] _processorTypeNameOptions = Array.Empty<string>();
        private int _selectedProcessorIndex;

        private SerializedProperty _modeChangeProcessor;
        private SerializedProperty _modeChangeProcessorTypeName;

        private void OnEnable()
        {
            _modeChangeProcessor = serializedObject.FindProperty("_modeChangeProcessor");
            _modeChangeProcessorTypeName = serializedObject.FindProperty("_modeChangeProcessorTypeName");
            RefreshProcessorTypes();
            SyncSelectedProcessorIndex();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(AlicizaEditorGUI.Styles.Panel);
            DrawProcessorSelector();
            DrawProcessorProperties();
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshProcessorTypes()
        {
            _processorTypeNames.Clear();
            _processorTypeNames.Add(NoneOptionName);

            IReadOnlyList<Type> types = AlicizaX.Utility.Assembly.GetRuntimeTypes(typeof(IUXNavigationModeChangeProcessor));
            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                if (type == null || type.IsAbstract || type.IsInterface || typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    continue;
                }

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                _processorTypeNames.Add(type.FullName);
            }

            _processorTypeNameOptions = _processorTypeNames.ToArray();
        }

        private void SyncSelectedProcessorIndex()
        {
            string typeName = _modeChangeProcessorTypeName != null ? _modeChangeProcessorTypeName.stringValue : string.Empty;
            if (string.IsNullOrEmpty(typeName) && _modeChangeProcessor != null && _modeChangeProcessor.managedReferenceValue != null)
            {
                typeName = _modeChangeProcessor.managedReferenceValue.GetType().FullName;
                _modeChangeProcessorTypeName.stringValue = typeName;
            }

            _selectedProcessorIndex = Mathf.Clamp(_processorTypeNames.IndexOf(typeName), 0, _processorTypeNames.Count - 1);
        }

        private void DrawProcessorSelector()
        {
            int newIndex = DrawStyledPopupRow("Processor Type", _selectedProcessorIndex, _processorTypeNameOptions);
            if (newIndex == _selectedProcessorIndex)
            {
                return;
            }

            UpdateProcessor(newIndex);
        }

        private void UpdateProcessor(int selectedIndex)
        {
            try
            {
                Undo.RecordObject(target, "Update UX Navigation Processor");
                if (selectedIndex == 0)
                {
                    ClearProcessor();
                    return;
                }

                string typeName = _processorTypeNames[selectedIndex];
                Type type = AlicizaX.Utility.Assembly.GetType(typeName) ?? Type.GetType(typeName);
                if (type == null || !typeof(IUXNavigationModeChangeProcessor).IsAssignableFrom(type))
                {
                    Debug.LogError($"Invalid UX navigation mode change processor type: {typeName}");
                    ClearProcessor();
                    return;
                }

                _modeChangeProcessor.managedReferenceValue = Activator.CreateInstance(type);
                _modeChangeProcessorTypeName.stringValue = typeName;
                _selectedProcessorIndex = selectedIndex;
                EditorUtility.SetDirty(target);
            }
            catch (Exception e)
            {
                Debug.LogError($"UX Navigation Processor Error: {e}");
                ClearProcessor();
            }
        }

        private void ClearProcessor()
        {
            _modeChangeProcessor.managedReferenceValue = null;
            _modeChangeProcessorTypeName.stringValue = string.Empty;
            _selectedProcessorIndex = 0;
            EditorUtility.SetDirty(target);
        }

        private void DrawProcessorProperties()
        {
            if (_modeChangeProcessor == null || _modeChangeProcessor.managedReferenceValue == null)
            {
                DrawMessageRow("Please select a Mode Change Processor", true);
                return;
            }

            Type processorType = _modeChangeProcessor.managedReferenceValue.GetType();
            SerializedProperty iterator = _modeChangeProcessor.Copy();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (!iterator.propertyPath.StartsWith(_modeChangeProcessor.propertyPath + ".", StringComparison.Ordinal))
                {
                    break;
                }

                DrawPropertyRow(iterator, processorType);
            }
        }

        private static void DrawSectionToolbar(string title)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(1f, SectionToolbarHeight, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

            Rect titleRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
            GUI.Label(titleRect, title, AlicizaEditorGUI.Styles.RowLabel);
        }

        private static int DrawStyledPopupRow(string label, int selectedIndex, string[] options)
        {
            EditorGUILayout.BeginHorizontal(AlicizaEditorGUI.Styles.FieldRow);
            EditorGUILayout.LabelField(label, AlicizaEditorGUI.Styles.FieldLabel, GUILayout.Width(PropertyLabelWidth));
            Rect popupRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
            int nextIndex = AlicizaEditorGUI.DrawStyledPopup(popupRect, selectedIndex, options);
            EditorGUILayout.EndHorizontal();
            return nextIndex;
        }

        private static void DrawPropertyRow(SerializedProperty property, Type ownerType)
        {
            EditorGUILayout.BeginHorizontal(AlicizaEditorGUI.Styles.FieldRow);
            EditorGUILayout.LabelField(GetPropertyLabel(property, ownerType), AlicizaEditorGUI.Styles.FieldLabel, GUILayout.Width(PropertyLabelWidth));
            EditorGUILayout.PropertyField(property, GUIContent.none, true);
            EditorGUILayout.EndHorizontal();
        }

        private static string GetPropertyLabel(SerializedProperty property, Type ownerType)
        {
            FieldInfo fieldInfo = FindField(ownerType, property.name);
            InspectorNameAttribute inspectorNameAttribute = fieldInfo != null
                ? Attribute.GetCustomAttribute(fieldInfo, typeof(InspectorNameAttribute)) as InspectorNameAttribute
                : null;

            if (inspectorNameAttribute != null && !string.IsNullOrEmpty(inspectorNameAttribute.displayName))
            {
                return inspectorNameAttribute.displayName;
            }

            return property.displayName;
        }

        private static FieldInfo FindField(Type ownerType, string fieldName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            while (ownerType != null)
            {
                FieldInfo fieldInfo = ownerType.GetField(fieldName, flags);
                if (fieldInfo != null)
                {
                    return fieldInfo;
                }

                ownerType = ownerType.BaseType;
            }

            return null;
        }

        private static void DrawMessageRow(string message, bool warning)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 34f, GUILayout.ExpandWidth(true));
            AlicizaEditorGUI.DrawBodyBackground(rect);
            GUI.Label(rect, message, warning ? AlicizaEditorGUI.Styles.WarningLabel : AlicizaEditorGUI.Styles.MutedMiniLabel);
        }
    }
}
#endif
