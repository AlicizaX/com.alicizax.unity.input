#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using AlicizaX;
using AlicizaX.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[CustomEditor(typeof(InputGlyphBehaviourBase), true)]
[CanEditMultipleObjects]
public sealed class InputGlyphEditor : Editor
{
    private const float ToolbarHeight = 30f;
    private const float RowHeight = 24f;
    private const float RowLabelWidth = 132f;
    private const float RefreshButtonWidth = 142f;
    private const float EventToolbarHeight = 26f;
    private const float EventActionButtonSize = 20f;

    private static readonly string[] ProfileIdOptions =
    {
        UXInput.Watch.InputProfile.KeyboardMouse.ToString(),
        UXInput.Watch.InputProfile.GenericGamepad.ToString(),
        UXInput.Watch.InputProfile.GenericJoystick.ToString(),
        UXInput.Watch.InputProfile.Xbox.ToString(),
        UXInput.Watch.InputProfile.PlayStation.ToString(),
        UXInput.Watch.InputProfile.Switch.ToString(),
        UXInput.Watch.InputProfile.SteamDeck.ToString(),
        UXInput.Watch.InputProfile.SteamController.ToString(),
        UXInput.Watch.InputProfile.Touch.ToString(),
    };

    private static readonly string[] ProfileDisplayOptions =
    {
        "Keyboard & Mouse",
        "Generic Gamepad",
        "Generic Joystick",
        "Xbox",
        "PlayStation",
        "Switch",
        "Steam Deck",
        "Steam Controller",
        "Touch",
    };

    private static readonly List<InputActionAsset> CachedActionAssets = new List<InputActionAsset>(16);
    private static bool _actionAssetCacheDirty = true;

    private readonly List<string> _compositePartBuffer = new List<string>(8);
    private readonly List<string> _compositeOptionBuffer = new List<string>(9);
    private readonly Dictionary<int, bool> _eventFoldouts = new Dictionary<int, bool>();
    private string[] _compositeOptionArray = Array.Empty<string>();
    private int _selectedEventIndex = -1;
    private SerializedProperty _actionSourceMode;
    private SerializedProperty _actionReference;
    private SerializedProperty _hotkeyTrigger;
    private SerializedProperty _actionName;
    private SerializedProperty _compositePartName;
    private SerializedProperty _targetImage;
    private SerializedProperty _targetText;
    private SerializedProperty _profileEvents;
    private GUIStyle _panelStyle;
    private GUIStyle _entryBodyStyle;
    private GUIStyle _fieldRowStyle;
    private GUIStyle _fieldLabelStyle;
    private GUIStyle _rowLabelStyle;
    private GUIStyle _mutedLabelStyle;
    private GUIStyle _mutedMiniLabelStyle;
    private GUIStyle _entryKindStyle;
    private GUIStyle _entryHandleStyle;
    private GUIStyle _emptyStateStyle;
    private GUIContent _addEventContent;
    private GUIContent _expandEventsContent;
    private GUIContent _collapseEventsContent;

    private void OnEnable()
    {
        _actionSourceMode = serializedObject.FindProperty("actionSourceMode");
        _actionReference = serializedObject.FindProperty("actionReference");
        _hotkeyTrigger = serializedObject.FindProperty("hotkeyTrigger");
        _actionName = serializedObject.FindProperty("actionName");
        _compositePartName = serializedObject.FindProperty("compositePartName");
        _targetImage = serializedObject.FindProperty("targetImage");
        _targetText = serializedObject.FindProperty("targetText");
        _profileEvents = serializedObject.FindProperty("profileEvents");
        _addEventContent = EditorGUIUtility.IconContent("Toolbar Plus", "Add profile event");
        _expandEventsContent = EditorGUIUtility.IconContent("d_scrollup", "Expand all profile events");
        _collapseEventsContent = EditorGUIUtility.IconContent("d_scrolldown", "Collapse all profile events");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EnsureStyles();

        EditorGUILayout.Space(6f);
        EditorGUILayout.BeginVertical(_panelStyle);
        DrawToolbar("Input Glyph");
        DrawSourceSection();
        DrawOutputSection();
        DrawEventsSection();
        EditorGUILayout.EndVertical();

        serializedObject.ApplyModifiedProperties();
    }

    private void EnsureStyles()
    {
        if (_panelStyle != null)
        {
            return;
        }

        _panelStyle = AlicizaEditorGUI.Styles.Panel;
        _entryBodyStyle = AlicizaEditorGUI.Styles.EntryBody;
        _fieldRowStyle = AlicizaEditorGUI.Styles.FieldRow;
        _fieldLabelStyle = AlicizaEditorGUI.Styles.FieldLabel;
        _rowLabelStyle = AlicizaEditorGUI.Styles.RowLabel;
        _mutedLabelStyle = AlicizaEditorGUI.Styles.MutedLabel;
        _mutedMiniLabelStyle = AlicizaEditorGUI.Styles.MutedMiniLabel;
        _entryKindStyle = AlicizaEditorGUI.Styles.KindBadge;
        _entryHandleStyle = AlicizaEditorGUI.Styles.Glyph;
        _emptyStateStyle = AlicizaEditorGUI.Styles.EmptyState;
    }

    private void DrawToolbar(string title)
    {
        Rect toolbarRect = GUILayoutUtility.GetRect(1f, ToolbarHeight, GUILayout.ExpandWidth(true));
        AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

        Rect labelRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 5f, toolbarRect.width - 16f, 20f);
        GUI.Label(labelRect, title, _rowLabelStyle);
    }

    private void DrawSourceSection()
    {
        InputAction resolvedAction = ResolveSelectedAction();

        DrawSectionBegin("Source");
        DrawEnumPropertyRow("Reference Mode", _actionSourceMode);
        DrawSourceFields();
        DrawResolvedActionInfo(resolvedAction);
        DrawCompositePartField(resolvedAction);
        DrawSectionEnd();
    }

    private void DrawSourceFields()
    {
        InputGlyphBehaviourBase.ActionSourceMode mode = (InputGlyphBehaviourBase.ActionSourceMode)_actionSourceMode.enumValueIndex;
        switch (mode)
        {
            case InputGlyphBehaviourBase.ActionSourceMode.ActionReference:
                DrawPropertyRow("Action Reference", _actionReference);
                EditorUtils.TrHelpIconText("Use a direct InputActionReference.", MessageType.None);
                break;

            case InputGlyphBehaviourBase.ActionSourceMode.HotkeyTrigger:
                DrawPropertyRow("Hotkey Trigger", _hotkeyTrigger);
                Component component = _hotkeyTrigger.objectReferenceValue as Component;
                if (component != null && !(component is HotkeyComponentBase))
                {
                    EditorUtils.TrHelpIconText("Hotkey Trigger must inherit HotkeyComponentBase.", MessageType.Warning);
                }
                else
                {
                    EditorUtils.TrHelpIconText("Reads the action from an external HotkeyComponentBase component.", MessageType.None);
                }

                break;

            case InputGlyphBehaviourBase.ActionSourceMode.ActionName:
                DrawPropertyRow("Action Name", _actionName);
                EditorGUILayout.BeginHorizontal(_fieldRowStyle);
                GUILayout.FlexibleSpace();
                if (AlicizaEditorGUI.DrawInlineButton("Refresh Action Cache", RefreshButtonWidth))
                {
                    _actionAssetCacheDirty = true;
                }

                EditorGUILayout.EndHorizontal();
                EditorUtils.TrHelpIconText("Use full action path: MapName/ActionName.", MessageType.None);
                break;
        }
    }

    private void DrawOutputSection()
    {
        DrawSectionBegin("Output");
        DrawOutputFields();
        DrawSectionEnd();
    }

    private void DrawOutputFields()
    {
        if (_targetImage != null)
        {
            DrawPropertyRow("Target Image", _targetImage);
            EditorUtils.TrHelpIconText("Shows the resolved sprite on a Unity UI Image.", MessageType.None);
            return;
        }

        if (_targetText != null)
        {
            DrawPropertyRow("Target TMP Text", _targetText);
            EditorUtils.TrHelpIconText("Uses the current TMP text as a template and replaces {0}.", MessageType.None);
            TMP_Text text = _targetText.objectReferenceValue as TMP_Text;
            if (text == null)
            {
                EditorUtils.TrHelpIconText("If TMP_Text is empty, the component tries GetComponent<TMP_Text>().", MessageType.None);
            }

            return;
        }

        EditorUtils.TrHelpIconText("This glyph component has no render target field.", MessageType.Warning);
    }

    private void DrawEventsSection()
    {
        DrawSectionBegin("Platform Events");
        DrawProfileEventList();
        DrawSectionEnd();
    }

    private void DrawProfileEventList()
    {
        int count = _profileEvents.arraySize;
        if (_selectedEventIndex >= count)
        {
            _selectedEventIndex = count - 1;
        }

        DrawProfileEventToolbar(count);
        if (count == 0)
        {
            DrawEmptyEventList();
            return;
        }

        for (int i = 0; i < count; i++)
        {
            DrawProfileEvent(i);
        }
    }

    private void DrawProfileEventToolbar(int count)
    {
        Rect toolbarRect = GUILayoutUtility.GetRect(1f, EventToolbarHeight, GUILayout.ExpandWidth(true));
        AlicizaEditorGUI.DrawToolbarBackground(toolbarRect);

        Rect addRect = new Rect(toolbarRect.xMax - EventActionButtonSize - 5f, toolbarRect.y + 3f, EventActionButtonSize, EventActionButtonSize);
        Rect expandRect = new Rect(addRect.x - EventActionButtonSize - 4f, addRect.y, EventActionButtonSize, EventActionButtonSize);
        Rect collapseRect = new Rect(expandRect.x - EventActionButtonSize - 4f, addRect.y, EventActionButtonSize, EventActionButtonSize);
        Rect titleRect = new Rect(toolbarRect.x + 8f, toolbarRect.y + 4f, Mathf.Max(40f, collapseRect.x - toolbarRect.x - 12f), 18f);

        GUI.Label(titleRect, $"Profile Events    {count}", _rowLabelStyle);

        if (AlicizaEditorGUI.DrawToolbarButton(collapseRect, _collapseEventsContent))
        {
            SetAllEventFoldouts(false);
        }

        if (AlicizaEditorGUI.DrawToolbarButton(expandRect, _expandEventsContent))
        {
            SetAllEventFoldouts(true);
        }

        if (AlicizaEditorGUI.DrawToolbarButton(addRect, _addEventContent))
        {
            AddProfileEvent();
        }
    }

    private void DrawEmptyEventList()
    {
        Rect rect = GUILayoutUtility.GetRect(1f, 42f, GUILayout.ExpandWidth(true));
        AlicizaEditorGUI.DrawBodyBackground(rect);
        GUI.Label(rect, "No profile events. Click + to add a profile rule.", _emptyStateStyle);
    }

    private void DrawProfileEvent(int index)
    {
        SerializedProperty eventProp = _profileEvents.GetArrayElementAtIndex(index);
        SerializedProperty profileIdProp = eventProp.FindPropertyRelative("profileId");
        SerializedProperty onMatchedProp = eventProp.FindPropertyRelative("onMatched");
        SerializedProperty onNotMatchedProp = eventProp.FindPropertyRelative("onNotMatched");

        bool expanded = IsEventExpanded(index);
        string profileName = string.IsNullOrWhiteSpace(profileIdProp.stringValue) ? "<Profile Id>" : profileIdProp.stringValue;
        string title = $"[{index}] {profileName}";
        string summary = BuildEventSummary(onMatchedProp, onNotMatchedProp);

        expanded = DrawProfileEventHeader(index, expanded, title, summary, profileName);
        _eventFoldouts[index] = expanded;

        if (expanded)
        {
            DrawProfileEventBody(profileIdProp, onMatchedProp, onNotMatchedProp);
        }
    }

    private bool DrawProfileEventHeader(int index, bool expanded, string title, string summary, string profileName)
    {
        Rect rowRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
        Event currentEvent = Event.current;
        bool selected = _selectedEventIndex == index;
        bool hovered = rowRect.Contains(currentEvent.mousePosition);
        AlicizaEditorGUI.DrawListItemBackground(rowRect, expanded || selected, hovered);

        Rect deleteRect = new Rect(rowRect.xMax - EventActionButtonSize - 1f, rowRect.y + 1f, EventActionButtonSize, rowRect.height - 2f);
        Rect clickRect = new Rect(rowRect.x, rowRect.y, deleteRect.x - rowRect.x, rowRect.height);
        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && clickRect.Contains(currentEvent.mousePosition))
        {
            _selectedEventIndex = index;
            _eventFoldouts[index] = !expanded;
            GUI.FocusControl(string.Empty);
            currentEvent.Use();
            return !expanded;
        }

        Rect handleRect = new Rect(rowRect.x + 7f, rowRect.y + 3f, 16f, rowRect.height - 6f);
        Rect foldRect = new Rect(handleRect.xMax + 2f, rowRect.y + 3f, 16f, rowRect.height - 6f);
        Rect badgeRect = new Rect(foldRect.xMax + 4f, rowRect.y + 3f, 74f, rowRect.height - 6f);
        Rect titleRect = new Rect(badgeRect.xMax + 6f, rowRect.y + 3f, Mathf.Max(92f, rowRect.width * 0.36f), rowRect.height - 6f);
        Rect summaryRect = new Rect(titleRect.xMax + 8f, rowRect.y + 3f, Mathf.Max(40f, deleteRect.x - titleRect.xMax - 12f), rowRect.height - 6f);

        GUI.Label(handleRect, "=", _entryHandleStyle);
        AlicizaEditorGUI.DrawFoldoutIcon(foldRect, expanded);
        GUI.Label(badgeRect, profileName, _entryKindStyle);
        GUI.Label(titleRect, title, _rowLabelStyle);
        GUI.Label(summaryRect, summary, _mutedMiniLabelStyle);

        if (AlicizaEditorGUI.DrawSymbolButton(deleteRect, "-"))
        {
            DeleteProfileEvent(index);
        }

        return expanded;
    }

    private void DrawProfileEventBody(
        SerializedProperty profileIdProp,
        SerializedProperty onMatchedProp,
        SerializedProperty onNotMatchedProp)
    {
        EditorGUILayout.BeginVertical(_entryBodyStyle);
        DrawProfileIdPopup(profileIdProp);
        DrawUnityEventRow("On Matched", onMatchedProp);
        DrawUnityEventRow("On Not Matched", onNotMatchedProp);
        EditorGUILayout.EndVertical();
    }

    private void DrawProfileIdPopup(SerializedProperty profileIdProp)
    {
        int selectedIndex = 0;
        for (int i = 0; i < ProfileIdOptions.Length; i++)
        {
            if (string.Equals(ProfileIdOptions[i], profileIdProp.stringValue, StringComparison.Ordinal))
            {
                selectedIndex = i;
                break;
            }
        }

        DrawPopupRow("Profile", selectedIndex, ProfileDisplayOptions, out int newIndex);
        profileIdProp.stringValue = ProfileIdOptions[newIndex];
    }

    private void DrawUnityEventRow(string label, SerializedProperty property)
    {
        EditorGUILayout.BeginVertical(_fieldRowStyle);
        EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        EditorGUILayout.EndVertical();
    }

    private void DrawResolvedActionInfo(InputAction action)
    {
        if (action == null)
        {
            return;
        }

        string mapName = action.actionMap != null ? action.actionMap.name : "<No Map>";
        DrawReadOnlyRow("Resolved Action", Utility.Text.Format("{0}/{1}", mapName, action.name), _mutedLabelStyle);
    }

    private void DrawCompositePartField(InputAction action)
    {
        CollectCompositePartNames(action, _compositePartBuffer);
        if (_compositePartBuffer.Count == 0)
        {
            if (!string.IsNullOrEmpty(_compositePartName.stringValue))
            {
                _compositePartName.stringValue = string.Empty;
            }

            return;
        }

        _compositeOptionBuffer.Clear();
        _compositeOptionBuffer.Add("<None>");
        for (int i = 0; i < _compositePartBuffer.Count; i++)
        {
            _compositeOptionBuffer.Add(_compositePartBuffer[i]);
        }

        EnsureCompositeOptionArray();

        int selectedIndex = 0;
        for (int i = 0; i < _compositePartBuffer.Count; i++)
        {
            if (string.Equals(_compositePartBuffer[i], _compositePartName.stringValue, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i + 1;
                break;
            }
        }

        DrawPopupRow("Composite Part", selectedIndex, _compositeOptionArray, out int newIndex);
        _compositePartName.stringValue = newIndex <= 0 ? string.Empty : _compositePartBuffer[newIndex - 1];
        EditorUtils.TrHelpIconText("Shown only when the resolved action contains composite bindings.", MessageType.None);
    }

    private void DrawSectionBegin(string title)
    {
        EditorGUILayout.Space(2f);
        Rect headerRect = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
        bool hovered = headerRect.Contains(Event.current.mousePosition);
        AlicizaEditorGUI.DrawListItemBackground(headerRect, true, hovered);

        Rect labelRect = new Rect(headerRect.x + 8f, headerRect.y + 3f, headerRect.width - 16f, 18f);
        GUI.Label(labelRect, title, _rowLabelStyle);
        EditorGUILayout.BeginVertical(_entryBodyStyle);
    }

    private static void DrawSectionEnd()
    {
        EditorGUILayout.EndVertical();
    }

    private void DrawEnumPropertyRow(string label, SerializedProperty property)
    {
        EditorGUILayout.BeginHorizontal(_fieldRowStyle);
        EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
        Rect popupRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
        property.enumValueIndex = AlicizaEditorGUI.DrawStyledPopup(popupRect, property.enumValueIndex, property.enumDisplayNames);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPopupRow(string label, int selectedIndex, string[] options, out int newIndex)
    {
        EditorGUILayout.BeginHorizontal(_fieldRowStyle);
        EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
        Rect popupRect = GUILayoutUtility.GetRect(90f, 20f, GUILayout.MinWidth(90f), GUILayout.ExpandWidth(true));
        newIndex = AlicizaEditorGUI.DrawStyledPopup(popupRect, selectedIndex, options);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPropertyRow(string label, SerializedProperty property, bool includeChildren = false)
    {
        EditorGUILayout.BeginHorizontal(_fieldRowStyle);
        EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
        EditorGUILayout.PropertyField(property, GUIContent.none, includeChildren);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawReadOnlyRow(string label, string value, GUIStyle valueStyle)
    {
        EditorGUILayout.BeginHorizontal(_fieldRowStyle);
        EditorGUILayout.LabelField(label, _fieldLabelStyle, GUILayout.Width(RowLabelWidth));
        EditorGUILayout.LabelField(value, valueStyle);
        EditorGUILayout.EndHorizontal();
    }

    private void AddProfileEvent()
    {
        Undo.RecordObjects(targets, "Add Input Glyph Profile Event");
        int index = _profileEvents.arraySize;
        _profileEvents.InsertArrayElementAtIndex(index);

        SerializedProperty eventProp = _profileEvents.GetArrayElementAtIndex(index);
        eventProp.FindPropertyRelative("profileId").stringValue = UXInput.Watch.InputProfile.KeyboardMouse.ToString();
        ClearUnityEvent(eventProp.FindPropertyRelative("onMatched"));
        ClearUnityEvent(eventProp.FindPropertyRelative("onNotMatched"));

        _selectedEventIndex = index;
        _eventFoldouts[index] = true;
        serializedObject.ApplyModifiedProperties();
        MarkTargetsDirty();
    }

    private void DeleteProfileEvent(int index)
    {
        if (index < 0 || index >= _profileEvents.arraySize)
        {
            return;
        }

        Undo.RecordObjects(targets, "Delete Input Glyph Profile Event");
        _profileEvents.DeleteArrayElementAtIndex(index);
        CleanupEventFoldouts(index);
        _selectedEventIndex = Mathf.Min(index, _profileEvents.arraySize - 1);
        serializedObject.ApplyModifiedProperties();
        MarkTargetsDirty();
        GUIUtility.ExitGUI();
    }

    private void MarkTargetsDirty()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            EditorUtility.SetDirty(targets[i]);
        }
    }

    private bool IsEventExpanded(int index)
    {
        return _eventFoldouts.TryGetValue(index, out bool expanded) && expanded;
    }

    private void SetAllEventFoldouts(bool expanded)
    {
        _eventFoldouts.Clear();
        for (int i = 0; i < _profileEvents.arraySize; i++)
        {
            _eventFoldouts[i] = expanded;
        }
    }

    private void CleanupEventFoldouts(int removedIndex)
    {
        _eventFoldouts.Remove(removedIndex);
        RemapBoolDictionary(_eventFoldouts, removedIndex);
    }

    private static void ClearUnityEvent(SerializedProperty property)
    {
        SerializedProperty calls = property.FindPropertyRelative("m_PersistentCalls.m_Calls");
        if (calls != null)
        {
            calls.ClearArray();
        }
    }

    private static string BuildEventSummary(SerializedProperty onMatchedProp, SerializedProperty onNotMatchedProp)
    {
        int matchedCount = GetPersistentCallCount(onMatchedProp);
        int notMatchedCount = GetPersistentCallCount(onNotMatchedProp);
        return $"matched {matchedCount}    not matched {notMatchedCount}";
    }

    private static int GetPersistentCallCount(SerializedProperty eventProp)
    {
        SerializedProperty calls = eventProp.FindPropertyRelative("m_PersistentCalls.m_Calls");
        return calls != null ? calls.arraySize : 0;
    }

    private static void RemapBoolDictionary(Dictionary<int, bool> dictionary, int removedIndex)
    {
        Dictionary<int, bool> remapped = new Dictionary<int, bool>();
        foreach (KeyValuePair<int, bool> pair in dictionary)
        {
            int nextIndex = pair.Key > removedIndex ? pair.Key - 1 : pair.Key;
            remapped[nextIndex] = pair.Value;
        }

        dictionary.Clear();
        foreach (KeyValuePair<int, bool> pair in remapped)
        {
            dictionary[pair.Key] = pair.Value;
        }
    }

    private InputAction ResolveSelectedAction()
    {
        InputGlyphBehaviourBase.ActionSourceMode mode = (InputGlyphBehaviourBase.ActionSourceMode)_actionSourceMode.enumValueIndex;
        switch (mode)
        {
            case InputGlyphBehaviourBase.ActionSourceMode.ActionReference:
                InputActionReference actionReference = _actionReference.objectReferenceValue as InputActionReference;
                return actionReference != null ? actionReference.action : null;

            case InputGlyphBehaviourBase.ActionSourceMode.HotkeyTrigger:
                Component component = _hotkeyTrigger.objectReferenceValue as Component;
                if (component is HotkeyComponentBase trigger && trigger.HotkeyAction != null)
                {
                    return trigger.HotkeyAction.action;
                }

                return null;

            case InputGlyphBehaviourBase.ActionSourceMode.ActionName:
                return ResolveActionByName(_actionName.stringValue);

            default:
                return null;
        }
    }

    private InputAction ResolveActionByName(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            return null;
        }

        foreach (InputActionAsset asset in EnumerateInputActionAssets())
        {
            if (asset == null)
            {
                continue;
            }

            InputAction action = asset.FindAction(actionName, false);
            if (action != null)
            {
                return action;
            }
        }

        return null;
    }

    private IEnumerable<InputActionAsset> EnumerateInputActionAssets()
    {
        EnsureActionAssetCache();
        for (int i = 0; i < CachedActionAssets.Count; i++)
        {
            yield return CachedActionAssets[i];
        }
    }

    private static void EnsureActionAssetCache()
    {
        if (!_actionAssetCacheDirty)
        {
            return;
        }

        _actionAssetCacheDirty = false;
        CachedActionAssets.Clear();
        string[] guids = AssetDatabase.FindAssets("t:InputActionAsset");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (asset != null && !CachedActionAssets.Contains(asset))
            {
                CachedActionAssets.Add(asset);
            }
        }
    }

    private static void CollectCompositePartNames(InputAction action, List<string> parts)
    {
        parts.Clear();
        if (action == null)
        {
            return;
        }

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (!binding.isPartOfComposite || string.IsNullOrWhiteSpace(binding.name))
            {
                continue;
            }

            if (!ContainsCompositePart(parts, binding.name))
            {
                parts.Add(binding.name);
            }
        }
    }

    private void EnsureCompositeOptionArray()
    {
        if (_compositeOptionArray.Length != _compositeOptionBuffer.Count)
        {
            _compositeOptionArray = new string[_compositeOptionBuffer.Count];
        }

        for (int i = 0; i < _compositeOptionBuffer.Count; i++)
        {
            _compositeOptionArray[i] = _compositeOptionBuffer[i];
        }
    }

    private static bool ContainsCompositePart(List<string> parts, string value)
    {
        for (int i = 0; i < parts.Count; i++)
        {
            if (string.Equals(parts[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

#endif
