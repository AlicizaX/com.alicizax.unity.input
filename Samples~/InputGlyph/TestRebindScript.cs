#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using AlicizaX;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class TestRebindScript : MonoBehaviour
{
    private const string NullBinding = "__NULL__";

    [Header("UI")]
    [SerializeField] private Button rebindButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button discardButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private TextMeshProUGUI bindingText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Image bindingIcon;

    [Header("Action")]
    [SerializeField] private InputActionReference actionReference;
    [Tooltip("Used when Action Reference is empty. Must be the full path, for example \"Gameplay/Jump\".")]
    [SerializeField] private string actionName = "Gameplay/Jump";
    [Tooltip("For composite bindings, use part names such as Up, Down, Left, or Right. Leave empty for a normal binding.")]
    [SerializeField] private string compositePartName;

    [Header("Behavior")]
    [SerializeField] private bool autoConfirm;

    private bool _isConfirming;
    private bool _hasPendingChange;
    private RebindChange _pendingChange;

    private void OnEnable()
    {
        if (rebindButton != null)
        {
            rebindButton.onClick.AddListener(StartRebind);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(ConfirmPrepared);
        }

        if (discardButton != null)
        {
            discardButton.onClick.AddListener(DiscardPrepared);
        }

        if (resetButton != null)
        {
            resetButton.onClick.AddListener(ResetToDefault);
        }

        UXInput.Watch.OnContextChanged += OnDeviceContextChanged;
        UXInput.Rebind.OnBindingsChanged += OnBindingsChanged;
        UXInput.Rebind.OnRebindPrepared += OnRebindPrepare;
        UXInput.Rebind.OnRebindStarted += OnRebindStart;
        UXInput.Rebind.OnRebindEnded += OnRebindEnd;
        UXInput.Rebind.OnBindingConflict += OnRebindConflict;
        UXInput.Rebind.OnApply += OnApply;

        RefreshView();
    }

    private void OnDisable()
    {
        if (rebindButton != null)
        {
            rebindButton.onClick.RemoveListener(StartRebind);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(ConfirmPrepared);
        }

        if (discardButton != null)
        {
            discardButton.onClick.RemoveListener(DiscardPrepared);
        }

        if (resetButton != null)
        {
            resetButton.onClick.RemoveListener(ResetToDefault);
        }

        UXInput.Watch.OnContextChanged -= OnDeviceContextChanged;
        UXInput.Rebind.OnBindingsChanged -= OnBindingsChanged;
        UXInput.Rebind.OnRebindPrepared -= OnRebindPrepare;
        UXInput.Rebind.OnRebindStarted -= OnRebindStart;
        UXInput.Rebind.OnRebindEnded -= OnRebindEnd;
        UXInput.Rebind.OnBindingConflict -= OnRebindConflict;
        UXInput.Rebind.OnApply -= OnApply;
    }

    private void StartRebind()
    {
        string resolvedActionName = ResolveActionName();
        if (string.IsNullOrEmpty(resolvedActionName))
        {
            SetStatus("Action is not configured.");
            return;
        }

        string partName = NormalizeCompositePartName();
        bool started = string.IsNullOrEmpty(partName)
            ? UXInput.Rebind.BeginRebind(resolvedActionName)
            : UXInput.Rebind.BeginCompositePartRebind(resolvedActionName, partName);

        if (!started)
        {
            SetStatus("Failed to start rebind.");
            RefreshButtons();
        }
    }

    private void ConfirmPrepared()
    {
        ConfirmPreparedInternal();
    }

    private void DiscardPrepared()
    {
        UXInput.Rebind.DiscardPrepared();
        _hasPendingChange = false;
        RefreshView();
    }

    private void ResetToDefault()
    {
        ResetToDefaultInternal();
    }

    private void OnDeviceContextChanged(UXInput.Watch.InputContext context)
    {
        RefreshView();
    }

    private void OnBindingsChanged()
    {
        RefreshView();
    }

    private void OnRebindStart()
    {
        SetStatus("Press a key. Esc cancels.");
        SetButtonsInteractable(false);
    }

    private void OnRebindPrepare(RebindChange change)
    {
        if (!IsTargetChange(change))
        {
            return;
        }

        _pendingChange = change;
        _hasPendingChange = true;
        if (bindingText != null)
        {
            bindingText.text = GetDisplayNameForOverridePath(change.OverridePath);
        }

        SetStatus("Pending apply.");
        SetButtonsInteractable(true);

        if (autoConfirm)
        {
            ConfirmPreparedInternal();
        }
    }

    private void OnRebindEnd(bool completed, RebindChange change)
    {
        if (change.IsValid && !IsTargetChange(change))
        {
            RefreshView();
            return;
        }

        if (!completed)
        {
            SetStatus("Rebind cancelled.");
        }

        SetButtonsInteractable(true);
        RefreshView();
    }

    private void OnRebindConflict(
        RebindChange prepared,
        IReadOnlyList<RebindChange> conflicts)
    {
        if (!IsTargetChange(prepared) && !ContainsTargetChange(conflicts))
        {
            return;
        }

        SetStatus("Conflict detected. Confirming will clear the conflicting binding.");
        RefreshView();
    }

    private void OnApply(bool applied, IReadOnlyList<RebindChange> changes)
    {
        if (!ContainsTargetChange(changes))
        {
            RefreshButtons();
            return;
        }

        _hasPendingChange = false;
        SetStatus(applied ? "Binding applied." : "Pending binding discarded.");
        RefreshView();
    }

    private bool ConfirmPreparedInternal()
    {
        if (_isConfirming)
        {
            return false;
        }

        _isConfirming = true;
        SetButtonsInteractable(false);

        try
        {
            bool applied = UXInput.Rebind.ConfirmApply();
            if (!applied)
            {
                SetStatus("No pending binding.");
            }

            return applied;
        }
        catch (Exception exception)
        {
            Log.Exception(exception);
            SetStatus("Failed to apply binding.");
            return false;
        }
        finally
        {
            _isConfirming = false;
            RefreshView();
        }
    }

    private void ResetToDefaultInternal()
    {
        SetButtonsInteractable(false);

        try
        {
            InputAction action = ResolveAction();
            if (action == null)
            {
                SetStatus("Action is not configured.");
                return;
            }

            string partName = NormalizeCompositePartName();
            if (string.IsNullOrEmpty(partName))
            {
                UXInput.Rebind.ResetBinding(action);
            }
            else
            {
                UXInput.Rebind.ResetCompositePartBinding(action, partName);
            }

            _hasPendingChange = false;
            SetStatus("Binding reset.");
        }
        catch (Exception exception)
        {
            Log.Exception(exception);
            SetStatus("Failed to reset bindings.");
        }
        finally
        {
            RefreshView();
        }
    }

    private void RefreshView()
    {
        InputAction action = ResolveAction();
        string partName = NormalizeCompositePartName();
        if (action == null)
        {
            if (bindingText != null)
            {
                bindingText.text = "<no action>";
            }

            if (bindingIcon != null)
            {
                bindingIcon.sprite = null;
                bindingIcon.enabled = false;
            }

            SetStatus("Action is not configured.");
            RefreshButtons();
            return;
        }

        if (bindingText != null)
        {
            string displayName = _hasPendingChange && IsTargetChange(_pendingChange)
                ? GetDisplayNameForOverridePath(_pendingChange.OverridePath)
                : UXInput.Glyph.GetDisplayNameFromInputAction(action, partName);
            bindingText.text = string.IsNullOrEmpty(displayName) ? "<unbound>" : displayName;
        }

        if (bindingIcon != null)
        {
            bool hasSprite = UXInput.Glyph.TryGetUISpriteForActionPath(action, partName, out Sprite sprite);
            bindingIcon.sprite = hasSprite ? sprite : null;
            bindingIcon.enabled = hasSprite;
        }

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        SetButtonsInteractable(!_isConfirming);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        bool hasPending = _hasPendingChange && UXInput.Rebind.HasPreparedRebinds;

        if (rebindButton != null)
        {
            rebindButton.interactable = interactable;
        }

        if (confirmButton != null)
        {
            confirmButton.interactable = interactable && hasPending;
        }

        if (discardButton != null)
        {
            discardButton.interactable = interactable && hasPending;
        }

        if (resetButton != null)
        {
            resetButton.interactable = interactable;
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private InputAction ResolveAction()
    {
        if (actionReference != null && actionReference.action != null)
        {
            return actionReference.action;
        }

        return InputActionProvider.TryResolveAction(actionName, out InputAction action) ? action : null;
    }

    private string ResolveActionName()
    {
        InputAction action = ResolveAction();
        if (action != null && action.actionMap != null)
        {
            return string.Concat(action.actionMap.name, "/", action.name);
        }

        return string.IsNullOrWhiteSpace(actionName) ? null : actionName;
    }

    private string NormalizeCompositePartName()
    {
        return string.IsNullOrWhiteSpace(compositePartName) ? null : compositePartName;
    }

    private string GetDisplayNameForOverridePath(string overridePath)
    {
        if (string.Equals(overridePath, NullBinding, StringComparison.Ordinal))
        {
            return "<cleared>";
        }

        string displayName = UXInput.Glyph.GetDisplayNameFromControlPath(overridePath);
        return string.IsNullOrEmpty(displayName) ? overridePath : displayName;
    }

    private bool IsTargetChange(RebindChange change)
    {
        if (!change.IsValid || change.Action == null)
        {
            return false;
        }

        InputAction targetAction = ResolveAction();
        if (targetAction == null || change.Action.id != targetAction.id)
        {
            return false;
        }

        return true;
    }

    private bool ContainsTargetChange(IReadOnlyList<RebindChange> changes)
    {
        if (changes == null)
        {
            return false;
        }

        for (int i = 0; i < changes.Count; i++)
        {
            if (IsTargetChange(changes[i]))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
