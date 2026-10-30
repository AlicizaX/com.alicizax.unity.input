#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using AlicizaX.Editor.UIToolkit;
using TMPro;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.UIElements;

[CustomEditor(typeof(InputGlyphBehaviourBase), true)]
[CanEditMultipleObjects]
public sealed class InputGlyphEditor : AlicizaUIEditor
{
    private const string ActionSourceModePropertyName = "actionSourceMode";
    private const string ActionReferencePropertyName = "actionReference";
    private const string HotkeyTriggerPropertyName = "hotkeyTrigger";
    private const string CompositePartNamePropertyName = "compositePartName";
    private const string TargetImagePropertyName = "targetImage";
    private const string TargetTextPropertyName = "targetText";
    private const string ProfileEventsPropertyName = "profileEvents";
    private const string ProfileIdPropertyName = "profileId";
    private const string OnMatchedPropertyName = "onMatched";
    private const string OnNotMatchedPropertyName = "onNotMatched";
    private const string PersistentCallsPropertyPath = "m_PersistentCalls.m_Calls";

    private const string NoneOptionLabel = "<None>";
    private const string ProfilePlaceholderLabel = "<Profile Id>";
    private const long RefreshIntervalMs = 200;

    private static readonly string[] ProfileOptionLabels = BuildProfileOptionLabels();
    private static readonly string[] ProfileOptionIds = BuildProfileOptionIds();

    private readonly List<string> _compositeParts = new List<string>(8);
    private readonly List<bool> _eventFoldouts = new List<bool>(8);
    private readonly List<EventCardBinding> _eventBindings = new List<EventCardBinding>(8);

    private SerializedProperty _actionSourceMode;
    private SerializedProperty _actionReference;
    private SerializedProperty _hotkeyTrigger;
    private SerializedProperty _compositePartName;
    private SerializedProperty _targetImage;
    private SerializedProperty _targetText;
    private SerializedProperty _profileEvents;
    private SerializedProperty _shownTarget;

    private AlicizaRow _actionReferenceRow;
    private AlicizaRow _hotkeyTriggerRow;
    private AlicizaRow _resolvedActionRow;
    private Label _resolvedActionLabel;
    private Label _targetHint;
    private Label _eventsCountBadge;
    private VisualElement _compositeHost;
    private VisualElement _eventsHost;
    private VisualElement _expandAllButton;
    private VisualElement _collapseAllButton;
    private VisualElement _addEventButton;

    private string _compositeSignature;
    private int _builtEventCount = -1;

    private bool CanEditStructure => !serializedObject.isEditingMultipleObjects;

    protected override void BuildInspector(VisualElement root)
    {
        CacheProperties();

        AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
        root.Add(panel);

        BuildSourceSection(panel.Body);
        BuildOutputSection(panel.Body);
        BuildEventsSection(panel.Body);

        Refresh();
        AlicizaEditorUI.RefreshEvery(root, Refresh, RefreshIntervalMs);
    }

    private void CacheProperties()
    {
        _actionSourceMode = Find(ActionSourceModePropertyName);
        _actionReference = Find(ActionReferencePropertyName);
        _hotkeyTrigger = Find(HotkeyTriggerPropertyName);
        _compositePartName = Find(CompositePartNamePropertyName);
        _targetImage = Find(TargetImagePropertyName);
        _targetText = Find(TargetTextPropertyName);
        _profileEvents = Find(ProfileEventsPropertyName);
    }

    private void Refresh()
    {
        if (serializedObject == null || serializedObject.targetObject == null)
        {
            return;
        }

        serializedObject.Update();
        RefreshSourceSection();
        RefreshTargetHint();
        RefreshEvents();
    }

    private void BuildSourceSection(VisualElement parent)
    {
        AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Source");
        parent.Add(section);

        if (_actionSourceMode != null)
        {
            var modeField = new AlicizaEnumField(_actionSourceMode);
            modeField.AddToClassList("alicia-control--grow");
            modeField.RegisterValueChangedCallback(evt => RefreshSourceSection());
            AlicizaRow modeRow = AlicizaEditorUI.AddRow(section.Body, "Reference Mode", modeField);
            SetCaptionTooltip(modeRow, "Where the glyph action comes from.");
        }
        else
        {
            section.Body.Add(AlicizaEditorUI.CreateHint(
                "Reference mode property was not found on this glyph component."));
        }

        _actionReferenceRow = AddObjectPropertyRow(
            section.Body,
            "Action Reference",
            _actionReference,
            typeof(InputActionReference),
            false,
            "Input action asset reference resolved for this glyph.");
        _hotkeyTriggerRow = AddObjectPropertyRow(
            section.Body,
            "Hotkey Trigger",
            _hotkeyTrigger,
            typeof(HotkeyComponentBase),
            true,
            "Hotkey component whose action is resolved for this glyph.");

        _resolvedActionRow = AlicizaEditorUI.AddValueRow(
            section.Body,
            "Resolved Action",
            string.Empty,
            AlicizaValueTone.Muted);
        _resolvedActionLabel = _resolvedActionRow.Content.Q<Label>();

        _compositeHost = new VisualElement { name = "input-glyph-composite-part" };
        section.Body.Add(_compositeHost);
    }

    private void BuildOutputSection(VisualElement parent)
    {
        AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Output");
        parent.Add(section);

        if (_targetImage != null)
        {
            _shownTarget = _targetImage;
            AlicizaRow row = AlicizaEditorUI.AddPropertyRow(
                section.Body,
                "Target Image",
                _targetImage,
                objectType: typeof(UnityEngine.UI.Image),
                allowSceneObjects: true);
            SetCaptionTooltip(row, "Image that receives the resolved glyph sprite.");
        }
        else if (_targetText != null)
        {
            _shownTarget = _targetText;
            AlicizaRow row = AlicizaEditorUI.AddPropertyRow(
                section.Body,
                "Target TMP Text",
                _targetText,
                objectType: typeof(TMP_Text),
                allowSceneObjects: true);
            SetCaptionTooltip(row, "Text component that receives the resolved glyph token.");
        }
        else
        {
            AlicizaEditorUI.AddValueRow(section.Body, "Render Target", "Not available", AlicizaValueTone.Warning);
            section.Body.Add(AlicizaEditorUI.CreateHint("This glyph component has no render target field."));
            return;
        }

        _targetHint = AlicizaEditorUI.CreateHint(
            "No render target is assigned. Glyph updates are skipped at runtime.");
        _targetHint.style.marginTop = 4f;
        _targetHint.style.display = DisplayStyle.None;
        section.Body.Add(_targetHint);
    }

    private void BuildEventsSection(VisualElement parent)
    {
        AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Platform Events");
        parent.Add(section);

        _eventsCountBadge = AlicizaEditorUI.CreateBadge("0", AlicizaValueTone.Muted);
        section.Header.Add(_eventsCountBadge);

        var actions = new VisualElement();
        actions.AddToClassList("alicia-actions");

        var collapseAll = AlicizaEditorUI.CreateTextButton(
            "Collapse All",
            "Collapse every profile event.");
        collapseAll.clicked += () => SetAllEventFoldouts(false);
        actions.Add(collapseAll);
        _collapseAllButton = collapseAll;

        var expandAll = AlicizaEditorUI.CreateTextButton(
            "Expand All",
            "Expand every profile event.");
        expandAll.clicked += () => SetAllEventFoldouts(true);
        actions.Add(expandAll);
        _expandAllButton = expandAll;

        var addEvent = AlicizaEditorUI.CreateTextButton(
            "Add Event",
            "Add a profile event rule.",
            AlicizaButtonTone.Accent);
        addEvent.clicked += AddProfileEvent;
        addEvent.SetEnabled(CanEditStructure);
        actions.Add(addEvent);
        _addEventButton = addEvent;

        section.Body.Add(actions);

        if (!CanEditStructure)
        {
            section.Body.Add(AlicizaEditorUI.CreateHint(
                "Add and remove are disabled while multiple glyph components are selected."));
        }

        _eventsHost = new VisualElement { name = "input-glyph-profile-events" };
        section.Body.Add(_eventsHost);
    }

    private void RefreshSourceSection()
    {
        int modeIndex = _actionSourceMode != null ? _actionSourceMode.enumValueIndex : -1;
        bool showReference = modeIndex == (int)InputGlyphBehaviourBase.ActionSourceMode.ActionReference;
        bool showHotkey = modeIndex == (int)InputGlyphBehaviourBase.ActionSourceMode.HotkeyTrigger;
        SetVisible(_actionReferenceRow, showReference);
        SetVisible(_hotkeyTriggerRow, showHotkey);

        InputAction action = ResolveSelectedAction();
        SetVisible(_resolvedActionRow, action != null);
        if (action != null && _resolvedActionLabel != null)
        {
            string mapName = action.actionMap != null ? action.actionMap.name : "<No Map>";
            _resolvedActionLabel.text = mapName + "/" + action.name;
            _resolvedActionLabel.tooltip = _resolvedActionLabel.text;
        }

        RefreshCompositePart(action);
    }

    private void RefreshCompositePart(InputAction action)
    {
        if (_compositeHost == null)
        {
            return;
        }

        CollectCompositePartNames(action, _compositeParts);
        string storedPart = _compositePartName != null ? _compositePartName.stringValue : string.Empty;
        string signature = BuildCompositeSignature(action, storedPart);
        if (string.Equals(signature, _compositeSignature, StringComparison.Ordinal))
        {
            return;
        }

        _compositeSignature = signature;
        _compositeHost.Clear();

        if (_compositeParts.Count == 0)
        {
            if (!string.IsNullOrEmpty(storedPart))
            {
                _compositeHost.Add(AlicizaEditorUI.CreateHint(
                    "Composite part '" + storedPart + "' is ignored because the resolved action has no composite parts."));
            }

            return;
        }

        if (_compositePartName == null)
        {
            _compositeHost.Add(AlicizaEditorUI.CreateHint(
                "Composite part property was not found on this glyph component."));
            return;
        }

        string[] options = new string[_compositeParts.Count + 1];
        options[0] = NoneOptionLabel;
        for (int i = 0; i < _compositeParts.Count; i++)
        {
            options[i + 1] = _compositeParts[i];
        }

        int selectedIndex = 0;
        string currentPart = _compositePartName.stringValue;
        for (int i = 0; i < _compositeParts.Count; i++)
        {
            if (string.Equals(_compositeParts[i], currentPart, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i + 1;
                break;
            }
        }

        var partField = new AlicizaEnumField(options, selectedIndex);
        partField.AddToClassList("alicia-control--grow");
        partField.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue < 0 || evt.newValue >= options.Length)
            {
                return;
            }

            string value = evt.newValue <= 0 ? string.Empty : options[evt.newValue];
            if (string.Equals(_compositePartName.stringValue, value, StringComparison.Ordinal))
            {
                return;
            }

            _compositePartName.stringValue = value;
            _compositePartName.serializedObject.ApplyModifiedProperties();
            MarkTargetsDirty();
        });

        AlicizaRow row = AlicizaEditorUI.AddRow(_compositeHost, "Composite Part", partField);
        SetCaptionTooltip(row, "Optional composite binding part, for example Up/Down/Left/Right.");
    }

    private void RefreshTargetHint()
    {
        if (_targetHint == null)
        {
            return;
        }

        bool assigned = _shownTarget != null && _shownTarget.objectReferenceValue != null;
        _targetHint.style.display = assigned ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void RefreshEvents()
    {
        if (_eventsHost == null)
        {
            return;
        }

        _profileEvents = Find(ProfileEventsPropertyName);
        if (_profileEvents == null)
        {
            if (_builtEventCount != -1)
            {
                _builtEventCount = -1;
                _eventBindings.Clear();
                _eventsHost.Clear();
            }

            if (_eventsHost.childCount == 0)
            {
                _eventsHost.Add(AlicizaEditorUI.CreateEmptyState(
                    "Profile events property was not found on this glyph component."));
            }

            if (_eventsCountBadge != null)
            {
                _eventsCountBadge.text = "—";
            }

            SetStructureActionsEnabled(false);
            return;
        }

        int count = _profileEvents.arraySize;
        EnsureEventFoldoutState(count);
        if (_eventsCountBadge != null)
        {
            _eventsCountBadge.text = count.ToString();
        }

        if (count != _builtEventCount || HasProfileOptionsChange())
        {
            RebuildEventCards(count);
        }

        for (int i = 0; i < _eventBindings.Count; i++)
        {
            UpdateEventCard(_eventBindings[i]);
        }
    }

    private void RebuildEventCards(int count)
    {
        _builtEventCount = count;
        _eventBindings.Clear();
        _eventsHost.Clear();
        SetStructureActionsEnabled(true);

        bool hasEvents = count > 0;
        if (_expandAllButton != null)
        {
            _expandAllButton.SetEnabled(hasEvents);
        }

        if (_collapseAllButton != null)
        {
            _collapseAllButton.SetEnabled(hasEvents);
        }

        if (count == 0)
        {
            _eventsHost.Add(AlicizaEditorUI.CreateEmptyState(
                "No profile events. Click Add Event to create a profile rule."));
            return;
        }

        for (int i = 0; i < count; i++)
        {
            BuildEventCard(i);
        }
    }

    private void BuildEventCard(int index)
    {
        SerializedProperty eventProp = GetEventProperty(index);
        if (eventProp == null)
        {
            return;
        }

        var binding = new EventCardBinding { Index = index };
        string profileId = ReadProfileId(eventProp);
        PrepareProfileOptions(binding, profileId);
        string profileLabel = string.IsNullOrWhiteSpace(profileId) ? ProfilePlaceholderLabel : profileId;

        AlicizaFoldoutCard card = AlicizaEditorUI.CreateFoldoutCard(
            "[" + index + "] " + profileLabel,
            IsEventExpanded(index));
        card.Meta.text = BuildEventSummary(eventProp);
        binding.Card = card;

        card.ExpandedChanged += expanded =>
        {
            if (binding.Index >= 0 && binding.Index < _eventFoldouts.Count)
            {
                _eventFoldouts[binding.Index] = expanded;
            }

            if (expanded && card.Body.childCount == 0)
            {
                BuildEventBody(binding);
            }
        };

        _eventsHost.Add(card);
        _eventBindings.Add(binding);

        if (card.Expanded)
        {
            BuildEventBody(binding);
        }
    }

    private void BuildEventBody(EventCardBinding binding)
    {
        SerializedProperty eventProp = GetEventProperty(binding.Index);
        if (eventProp == null || binding.Card == null)
        {
            return;
        }

        VisualElement body = binding.Card.Body;
        body.Clear();

        SerializedProperty profileIdProp = eventProp.FindPropertyRelative(ProfileIdPropertyName);
        if (profileIdProp == null)
        {
            body.Add(AlicizaEditorUI.CreateHint("Profile id property was not found on this profile event."));
        }
        else
        {
            int selectedIndex = IndexOfId(binding.ProfileOptionIds, profileIdProp.stringValue);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            var profileField = new AlicizaEnumField(binding.ProfileOptionLabels, selectedIndex);
            profileField.AddToClassList("alicia-control--grow");
            profileField.RegisterValueChangedCallback(evt => OnProfileSelected(binding, evt.newValue));
            AlicizaRow profileRow = AlicizaEditorUI.AddRow(body, "Profile", profileField);
            SetCaptionTooltip(profileRow, "Platform profile that triggers this event rule.");
            binding.ProfileField = profileField;

            binding.ProfileHint = AlicizaEditorUI.CreateHint(string.Empty);
            binding.ProfileHint.style.display = DisplayStyle.None;
            body.Add(binding.ProfileHint);
        }

        AddUnityEventField(body, "On Matched", eventProp.FindPropertyRelative(OnMatchedPropertyName));
        AddUnityEventField(body, "On Not Matched", eventProp.FindPropertyRelative(OnNotMatchedPropertyName));

        var actions = new VisualElement();
        actions.AddToClassList("alicia-actions");

        var removeButton = AlicizaEditorUI.CreateTextButton(
            "Remove Event",
            "Delete this profile event.",
            AlicizaButtonTone.Danger);
        removeButton.clicked += () => DeleteProfileEvent(binding.Index);
        removeButton.SetEnabled(CanEditStructure);
        actions.Add(removeButton);
        body.Add(actions);
    }

    private void UpdateEventCard(EventCardBinding binding)
    {
        SerializedProperty eventProp = GetEventProperty(binding.Index);
        if (eventProp == null || binding.Card == null)
        {
            return;
        }

        string profileId = ReadProfileId(eventProp);
        string profileLabel = string.IsNullOrWhiteSpace(profileId) ? ProfilePlaceholderLabel : profileId;
        binding.Card.Title.text = "[" + binding.Index + "] " + profileLabel;
        binding.Card.Meta.text = BuildEventSummary(eventProp);

        if (binding.ProfileField != null)
        {
            int selectedIndex = IndexOfId(binding.ProfileOptionIds, profileId);
            if (selectedIndex >= 0 && binding.ProfileField.value != selectedIndex)
            {
                binding.ProfileField.SetValueWithoutNotify(selectedIndex);
            }
        }

        if (binding.ProfileHint == null)
        {
            return;
        }

        bool unknown = !string.IsNullOrEmpty(profileId) && IndexOfId(ProfileOptionIds, profileId) < 0;
        bool showHint = unknown && string.IsNullOrEmpty(binding.MissingProfileId);
        binding.ProfileHint.style.display = showHint ? DisplayStyle.Flex : DisplayStyle.None;
        if (showHint)
        {
            binding.ProfileHint.text =
                "Profile id '" + profileId + "' is not in the editable profile catalog. Choose a valid profile.";
        }
    }

    private bool HasProfileOptionsChange()
    {
        for (int i = 0; i < _eventBindings.Count; i++)
        {
            EventCardBinding binding = _eventBindings[i];
            if (binding.ProfileField == null)
            {
                continue;
            }

            string profileId = ReadProfileId(binding.Index);
            bool unknown = !string.IsNullOrEmpty(profileId) && IndexOfId(ProfileOptionIds, profileId) < 0;
            if (!unknown)
            {
                if (!string.IsNullOrEmpty(binding.MissingProfileId))
                {
                    return true;
                }

                continue;
            }

            if (!string.Equals(binding.MissingProfileId, profileId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void OnProfileSelected(EventCardBinding binding, int optionIndex)
    {
        if (binding.ProfileOptionIds == null ||
            optionIndex < 0 ||
            optionIndex >= binding.ProfileOptionIds.Length)
        {
            return;
        }

        string profileId = binding.ProfileOptionIds[optionIndex];
        if (profileId == null)
        {

            return;
        }

        SerializedProperty eventProp = GetEventProperty(binding.Index);
        SerializedProperty profileIdProp = eventProp != null
            ? eventProp.FindPropertyRelative(ProfileIdPropertyName)
            : null;
        if (profileIdProp == null ||
            string.Equals(profileIdProp.stringValue, profileId, StringComparison.Ordinal))
        {
            return;
        }

        profileIdProp.stringValue = profileId;
        profileIdProp.serializedObject.ApplyModifiedProperties();
        MarkTargetsDirty();
        UpdateEventCard(binding);
    }

    private void AddProfileEvent()
    {
        if (!CanEditStructure)
        {
            return;
        }

        serializedObject.Update();
        SerializedProperty events = Find(ProfileEventsPropertyName);
        if (events == null)
        {
            return;
        }

        Undo.RecordObjects(targets, "Add Input Glyph Profile Event");
        int index = events.arraySize;
        events.InsertArrayElementAtIndex(index);

        SerializedProperty eventProp = events.GetArrayElementAtIndex(index);
        SerializedProperty profileId = eventProp.FindPropertyRelative(ProfileIdPropertyName);
        if (profileId != null)
        {
            profileId.stringValue = InputProfileCatalog.GetId(UXInput.Watch.InputProfile.KeyboardMouse);
        }

        ClearUnityEvent(eventProp.FindPropertyRelative(OnMatchedPropertyName));
        ClearUnityEvent(eventProp.FindPropertyRelative(OnNotMatchedPropertyName));

        serializedObject.ApplyModifiedProperties();
        MarkTargetsDirty();

        EnsureEventFoldoutState(index + 1);
        _eventFoldouts[index] = true;
        Refresh();
    }

    private void DeleteProfileEvent(int index)
    {
        if (!CanEditStructure)
        {
            return;
        }

        serializedObject.Update();
        SerializedProperty events = Find(ProfileEventsPropertyName);
        if (events == null || index < 0 || index >= events.arraySize)
        {
            return;
        }

        if (index < _eventFoldouts.Count)
        {
            _eventFoldouts.RemoveAt(index);
        }

        Undo.RecordObjects(targets, "Delete Input Glyph Profile Event");
        events.DeleteArrayElementAtIndex(index);
        serializedObject.ApplyModifiedProperties();
        MarkTargetsDirty();
        Refresh();
    }

    private void SetAllEventFoldouts(bool expanded)
    {
        int count = _profileEvents != null ? _profileEvents.arraySize : _eventBindings.Count;
        EnsureEventFoldoutState(count);
        for (int i = 0; i < _eventFoldouts.Count; i++)
        {
            _eventFoldouts[i] = expanded;
        }

        for (int i = 0; i < _eventBindings.Count; i++)
        {
            EventCardBinding binding = _eventBindings[i];
            if (binding.Card == null)
            {
                continue;
            }

            binding.Card.SetExpanded(expanded, false);
            if (expanded && binding.Card.Body.childCount == 0)
            {
                BuildEventBody(binding);
            }
        }
    }

    private void SetStructureActionsEnabled(bool enabled)
    {
        if (_addEventButton != null)
        {
            _addEventButton.SetEnabled(enabled && CanEditStructure);
        }

        if (_expandAllButton != null)
        {
            _expandAllButton.SetEnabled(enabled);
        }

        if (_collapseAllButton != null)
        {
            _collapseAllButton.SetEnabled(enabled);
        }
    }

    private void EnsureEventFoldoutState(int count)
    {
        if (count < 0)
        {
            count = 0;
        }

        while (_eventFoldouts.Count < count)
        {
            _eventFoldouts.Add(false);
        }

        while (_eventFoldouts.Count > count)
        {
            _eventFoldouts.RemoveAt(_eventFoldouts.Count - 1);
        }
    }

    private bool IsEventExpanded(int index)
    {
        return index >= 0 && index < _eventFoldouts.Count && _eventFoldouts[index];
    }

    private SerializedProperty GetEventProperty(int index)
    {
        if (_profileEvents == null)
        {
            _profileEvents = Find(ProfileEventsPropertyName);
        }

        if (_profileEvents == null || index < 0 || index >= _profileEvents.arraySize)
        {
            return null;
        }

        return _profileEvents.GetArrayElementAtIndex(index);
    }

    private static string ReadProfileId(SerializedProperty eventProperty)
    {
        SerializedProperty profileId = eventProperty != null
            ? eventProperty.FindPropertyRelative(ProfileIdPropertyName)
            : null;
        return profileId != null ? profileId.stringValue : string.Empty;
    }

    private string ReadProfileId(int index)
    {
        return ReadProfileId(GetEventProperty(index));
    }

    private static void PrepareProfileOptions(EventCardBinding binding, string profileId)
    {
        bool unknown = !string.IsNullOrEmpty(profileId) && IndexOfId(ProfileOptionIds, profileId) < 0;
        if (!unknown)
        {
            binding.ProfileOptionLabels = ProfileOptionLabels;
            binding.ProfileOptionIds = ProfileOptionIds;
            binding.MissingProfileId = null;
            return;
        }

        string[] labels = new string[ProfileOptionLabels.Length + 1];
        labels[0] = "<Missing: " + profileId + ">";
        Array.Copy(ProfileOptionLabels, 0, labels, 1, ProfileOptionLabels.Length);

        string[] ids = new string[ProfileOptionIds.Length + 1];
        ids[0] = null;
        Array.Copy(ProfileOptionIds, 0, ids, 1, ProfileOptionIds.Length);

        binding.ProfileOptionLabels = labels;
        binding.ProfileOptionIds = ids;
        binding.MissingProfileId = profileId;
    }

    private static void AddUnityEventField(VisualElement parent, string label, SerializedProperty property)
    {
        if (property == null)
        {
            parent.Add(AlicizaEditorUI.CreateHint(
                label + " property was not found on this profile event."));
            return;
        }

        var field = new PropertyField(property, label);
        field.AddToClassList("alicia-control");
        field.BindProperty(property);
        parent.Add(field);
    }

    private static string BuildEventSummary(SerializedProperty eventProperty)
    {
        int matchedCount = GetPersistentCallCount(
            eventProperty != null ? eventProperty.FindPropertyRelative(OnMatchedPropertyName) : null);
        int notMatchedCount = GetPersistentCallCount(
            eventProperty != null ? eventProperty.FindPropertyRelative(OnNotMatchedPropertyName) : null);
        return "matched " + matchedCount + "    not matched " + notMatchedCount;
    }

    private static int GetPersistentCallCount(SerializedProperty eventProperty)
    {
        SerializedProperty calls = eventProperty != null
            ? eventProperty.FindPropertyRelative(PersistentCallsPropertyPath)
            : null;
        return calls != null ? calls.arraySize : 0;
    }

    private static void ClearUnityEvent(SerializedProperty eventProperty)
    {
        SerializedProperty calls = eventProperty != null
            ? eventProperty.FindPropertyRelative(PersistentCallsPropertyPath)
            : null;
        if (calls != null)
        {
            calls.ClearArray();
        }
    }

    private InputAction ResolveSelectedAction()
    {
        if (_actionSourceMode == null)
        {
            return null;
        }

        InputGlyphBehaviourBase.ActionSourceMode mode =
            (InputGlyphBehaviourBase.ActionSourceMode)_actionSourceMode.enumValueIndex;
        switch (mode)
        {
            case InputGlyphBehaviourBase.ActionSourceMode.ActionReference:
                InputActionReference actionReference =
                    _actionReference != null ? _actionReference.objectReferenceValue as InputActionReference : null;
                return actionReference != null ? actionReference.action : null;

            case InputGlyphBehaviourBase.ActionSourceMode.HotkeyTrigger:
                HotkeyComponentBase trigger =
                    _hotkeyTrigger != null ? _hotkeyTrigger.objectReferenceValue as HotkeyComponentBase : null;
                return trigger != null && trigger.HotkeyAction != null ? trigger.HotkeyAction.action : null;

            default:
                return null;
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

    private string BuildCompositeSignature(InputAction action, string storedPart)
    {
        if (_compositeParts.Count == 0)
        {
            string actionId = action != null ? action.id.ToString() : "none";
            return actionId + "||" + storedPart;
        }

        return action.id.ToString() + "|" + string.Join(";", _compositeParts);
    }

    private AlicizaRow AddObjectPropertyRow(
        VisualElement parent,
        string caption,
        SerializedProperty property,
        Type objectType,
        bool allowSceneObjects,
        string tooltip)
    {
        if (property == null)
        {
            parent.Add(AlicizaEditorUI.CreateHint(
                caption + " property was not found on this glyph component."));
            return null;
        }

        AlicizaRow row = AlicizaEditorUI.AddPropertyRow(
            parent,
            caption,
            property,
            objectType: objectType,
            allowSceneObjects: allowSceneObjects);
        SetCaptionTooltip(row, tooltip);
        return row;
    }

    private void MarkTargetsDirty()
    {
        for (int i = 0; i < targets.Length; i++)
        {
            EditorUtility.SetDirty(targets[i]);
        }
    }

    private static void SetVisible(VisualElement element, bool visible)
    {
        if (element == null)
        {
            return;
        }

        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void SetCaptionTooltip(AlicizaRow row, string tooltip)
    {
        if (row != null && row.Caption != null)
        {
            row.Caption.tooltip = tooltip;
        }
    }

    private static int IndexOfId(string[] ids, string id)
    {
        if (ids == null)
        {
            return -1;
        }

        for (int i = 0; i < ids.Length; i++)
        {
            if (string.Equals(ids[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string[] BuildProfileOptionLabels()
    {
        IReadOnlyList<InputProfileInfo> profiles = InputProfileCatalog.Editable;
        string[] labels = new string[profiles.Count + 1];
        labels[0] = NoneOptionLabel;
        for (int i = 0; i < profiles.Count; i++)
        {
            labels[i + 1] = profiles[i].DisplayName;
        }

        return labels;
    }

    private static string[] BuildProfileOptionIds()
    {
        IReadOnlyList<InputProfileInfo> profiles = InputProfileCatalog.Editable;
        string[] ids = new string[profiles.Count + 1];
        ids[0] = string.Empty;
        for (int i = 0; i < profiles.Count; i++)
        {
            ids[i + 1] = profiles[i].Id;
        }

        return ids;
    }

    private sealed class EventCardBinding
    {
        public int Index;
        public AlicizaFoldoutCard Card;
        public AlicizaEnumField ProfileField;
        public Label ProfileHint;
        public string[] ProfileOptionLabels;
        public string[] ProfileOptionIds;
        public string MissingProfileId;
    }
}

#endif
