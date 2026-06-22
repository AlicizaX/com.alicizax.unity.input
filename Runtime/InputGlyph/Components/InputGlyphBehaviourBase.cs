#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public abstract class InputGlyphBehaviourBase : MonoBehaviour
{
    public enum ActionSourceMode
    {
        ActionReference,
        HotkeyTrigger,
        ActionName
    }

    [Serializable]
    public sealed class DeviceProfileEvent
    {
        public string profileId;
        public UnityEvent onMatched;
        public UnityEvent onNotMatched;
    }

    [Header("Source")] [SerializeField] private ActionSourceMode actionSourceMode = ActionSourceMode.ActionReference;
    [SerializeField] private InputActionReference actionReference;
    [SerializeField] private Component hotkeyTrigger;
    [SerializeField] private string actionName;
    [SerializeField] private string compositePartName;

    [Header("Platform Events")] [SerializeField]
    private DeviceProfileEvent[] profileEvents = Array.Empty<DeviceProfileEvent>();

    private bool _hasInvokedProfileEvent;
    private string _lastInvokedProfileId;

    protected string CurrentProfileId => UXInput.Glyph.CurrentProfileId;
    protected string CompositePartName => compositePartName;

#if UNITY_EDITOR
    protected virtual void OnValidate()
    {
        AutoAssignHotkeyTrigger();
        AutoAssignTarget();
    }
#endif

    protected virtual void OnEnable()
    {
        AutoAssignHotkeyTrigger();
        AutoAssignTarget();
        UXInput.Watch.OnContextChanged += HandleInputContextChanged;
        UXInput.Rebind.OnBindingsChanged += HandleBindingsChanged;
        RefreshGlyph();
        InvokeProfileEvents(true);
    }

    protected virtual void OnDisable()
    {
        UXInput.Watch.OnContextChanged -= HandleInputContextChanged;
        UXInput.Rebind.OnBindingsChanged -= HandleBindingsChanged;
    }

    private void HandleInputContextChanged(UXInput.Watch.InputContext context)
    {
        InvokeProfileEvents(false);
        RefreshGlyph();
    }

    private void HandleBindingsChanged()
    {
        RefreshGlyph();
    }

    protected InputAction ResolveAction()
    {
        switch (actionSourceMode)
        {
            case ActionSourceMode.ActionReference:
                return actionReference != null ? actionReference.action : null;
            case ActionSourceMode.HotkeyTrigger:
                return ResolveHotkeyAction();
            case ActionSourceMode.ActionName:
                return InputActionProvider.ResolveAction(actionName);
            default:
                return null;
        }
    }

    protected virtual void AutoAssignTarget()
    {
    }

    private InputAction ResolveHotkeyAction()
    {
        HotkeyComponentBase trigger = ResolveHotkeyTrigger();
        return trigger != null && trigger.HotkeyAction != null ? trigger.HotkeyAction.action : null;
    }

    private HotkeyComponentBase ResolveHotkeyTrigger()
    {
        return hotkeyTrigger as HotkeyComponentBase;
    }

    private void AutoAssignHotkeyTrigger()
    {
        if (actionSourceMode != ActionSourceMode.HotkeyTrigger || hotkeyTrigger != null)
        {
            return;
        }

        if (TryGetComponent(typeof(HotkeyComponentBase), out Component component))
        {
            hotkeyTrigger = component;
        }
    }

    private void InvokeProfileEvents(bool force)
    {
        string currentProfileId = CurrentProfileId;
        if (!force && _hasInvokedProfileEvent && string.Equals(_lastInvokedProfileId, currentProfileId, StringComparison.Ordinal))
        {
            return;
        }

        _hasInvokedProfileEvent = true;
        _lastInvokedProfileId = currentProfileId;
        if (profileEvents == null)
        {
            return;
        }

        for (int i = 0; i < profileEvents.Length; i++)
        {
            DeviceProfileEvent profileEvent = profileEvents[i];
            if (profileEvent == null)
            {
                continue;
            }

            if (string.Equals(profileEvent.profileId, currentProfileId, StringComparison.OrdinalIgnoreCase))
            {
                profileEvent.onMatched?.Invoke();
            }
            else
            {
                profileEvent.onNotMatched?.Invoke();
            }
        }
    }

    protected abstract void RefreshGlyph();
}
#endif
