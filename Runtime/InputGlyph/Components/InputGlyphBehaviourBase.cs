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
        HotkeyTrigger
    }

    [Serializable]
    public sealed class DeviceProfileEvent
    {
        public string profileId;
        public UnityEvent onMatched;
        public UnityEvent onNotMatched;
    }

    [Header("Source")]
    [SerializeField] private ActionSourceMode actionSourceMode = ActionSourceMode.ActionReference;
    [SerializeField] private InputActionReference actionReference;
    [SerializeField] private HotkeyComponentBase hotkeyTrigger;
    [SerializeField] private string compositePartName;

    [Header("Platform Events")]
    [SerializeField] private DeviceProfileEvent[] profileEvents = Array.Empty<DeviceProfileEvent>();

    private InputAction _runtimeAction;
    private string _runtimeCompositePartName;
    private bool _useRuntimeAction;
    private string _lastInvokedProfileId;

    protected string CurrentProfileId => UXInput.Glyph.CurrentProfileId;
    protected string CompositePartName => _useRuntimeAction ? _runtimeCompositePartName : compositePartName;

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

    /// <summary>
    /// 运行时动态切换用于图标解析的 InputAction，并立即刷新显示。
    /// </summary>
    /// <param name="action">目标 Action；传 null 清空显示。</param>
    /// <param name="compositePartName">
    /// Composite 子部分名，例如 Move 的 "Up"/"Down"/"Left"/"Right"。
    /// 普通绑定传 null 或空字符串。
    /// </param>
    public void SetAction(InputAction action, string compositePartName = null)
    {
        _useRuntimeAction = true;
        _runtimeAction = action;
        _runtimeCompositePartName = compositePartName;
        if (isActiveAndEnabled)
        {
            RefreshGlyph();
        }
    }

    /// <summary>
    /// 清除运行时 Action 覆盖，恢复为 Inspector 配置的数据源。
    /// </summary>
    public void ClearRuntimeAction()
    {
        if (!_useRuntimeAction)
        {
            return;
        }

        _useRuntimeAction = false;
        _runtimeAction = null;
        _runtimeCompositePartName = null;
        if (isActiveAndEnabled)
        {
            RefreshGlyph();
        }
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
        if (_useRuntimeAction)
        {
            return _runtimeAction;
        }

        switch (actionSourceMode)
        {
            case ActionSourceMode.ActionReference:
                return actionReference != null ? actionReference.action : null;
            case ActionSourceMode.HotkeyTrigger:
                return hotkeyTrigger != null && hotkeyTrigger.HotkeyAction != null
                    ? hotkeyTrigger.HotkeyAction.action
                    : null;
            default:
                return null;
        }
    }

    protected virtual void AutoAssignTarget()
    {
    }

    private void AutoAssignHotkeyTrigger()
    {
        if (actionSourceMode != ActionSourceMode.HotkeyTrigger || hotkeyTrigger != null)
        {
            return;
        }

        hotkeyTrigger = GetComponent<HotkeyComponentBase>();
    }

    private void InvokeProfileEvents(bool force)
    {
        string currentProfileId = CurrentProfileId;
        if (!force && string.Equals(_lastInvokedProfileId, currentProfileId, StringComparison.Ordinal))
        {
            return;
        }

        _lastInvokedProfileId = currentProfileId;
        for (int i = 0; i < profileEvents.Length; i++)
        {
            DeviceProfileEvent profileEvent = profileEvents[i];
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
