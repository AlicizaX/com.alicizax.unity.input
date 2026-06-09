#if INPUTSYSTEM_SUPPORT
using System;
using System.Threading.Tasks;
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

        InputDeviceWatcher.OnDeviceContextChanged += OnDeviceContextChanged;
        InputBindingManager.BindingsChanged += OnBindingsChanged;
        InputBindingManager.OnRebindPrepare += OnRebindPrepare;
        InputBindingManager.OnRebindStart += OnRebindStart;
        InputBindingManager.OnRebindEnd += OnRebindEnd;
        InputBindingManager.OnRebindConflict += OnRebindConflict;
        InputBindingManager.OnApply += OnApply;

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

        InputDeviceWatcher.OnDeviceContextChanged -= OnDeviceContextChanged;
        InputBindingManager.BindingsChanged -= OnBindingsChanged;
        InputBindingManager.OnRebindPrepare -= OnRebindPrepare;
        InputBindingManager.OnRebindStart -= OnRebindStart;
        InputBindingManager.OnRebindEnd -= OnRebindEnd;
        InputBindingManager.OnRebindConflict -= OnRebindConflict;
        InputBindingManager.OnApply -= OnApply;
    }

    private void StartRebind()
    {
        string resolvedActionName = ResolveActionName();
        if (string.IsNullOrEmpty(resolvedActionName))
        {
            SetStatus("Action is not configured.");
            return;
        }

        InputBindingManager.StartRebind(resolvedActionName, NormalizeCompositePartName());
    }

    private async void ConfirmPrepared()
    {
        await ConfirmPreparedAsync();
    }

    private void DiscardPrepared()
    {
        InputBindingManager.DiscardPrepared();
        RefreshView();
    }

    private async void ResetToDefault()
    {
        await ResetToDefaultAsync();
    }

    private void OnDeviceContextChanged(InputGlyphContext context)
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

    private void OnRebindPrepare(InputBindingManager.RebindContext context)
    {
        if (!IsTargetContext(context))
        {
            return;
        }

        if (bindingText != null)
        {
            bindingText.text = GetDisplayNameForOverridePath(context.OverridePath);
        }

        SetStatus("Pending apply.");
        SetButtonsInteractable(true);

        if (autoConfirm)
        {
            _ = ConfirmPreparedAsync();
        }
    }

    private void OnRebindEnd(bool completed, InputBindingManager.RebindContext context)
    {
        if (context != null && !IsTargetContext(context))
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
        InputBindingManager.RebindContext prepared,
        InputBindingManager.RebindContext conflict)
    {
        if (!IsTargetContext(prepared) && !IsTargetContext(conflict))
        {
            return;
        }

        SetStatus("Conflict detected. Confirming will clear the conflicting binding.");
        RefreshView();
    }

    private void OnApply(bool applied, InputBindingManager.RebindContext[] contexts)
    {
        if (!ContainsTargetContext(contexts))
        {
            RefreshButtons();
            return;
        }

        SetStatus(applied ? "Binding applied." : "Pending binding discarded.");
        RefreshView();
    }

    private async Task<bool> ConfirmPreparedAsync()
    {
        if (_isConfirming)
        {
            return false;
        }

        _isConfirming = true;
        SetButtonsInteractable(false);

        try
        {
            bool applied = await InputBindingManager.ConfirmApply();
            if (!applied)
            {
                SetStatus("No pending binding.");
            }

            return applied;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            SetStatus("Failed to apply binding.");
            return false;
        }
        finally
        {
            _isConfirming = false;
            RefreshView();
        }
    }

    private async Task ResetToDefaultAsync()
    {
        SetButtonsInteractable(false);

        try
        {
            await InputBindingManager.ResetToDefaultAsync();
            SetStatus("Bindings reset.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
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
        InputGlyphContext context = InputDeviceWatcher.CurrentContext;

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
            string displayName = TryGetPendingTargetContext(out InputBindingManager.RebindContext pendingContext)
                ? GetDisplayNameForOverridePath(pendingContext.OverridePath)
                : InputGlyphService.GetDisplayNameFromInputAction(action, partName, context);
            bindingText.text = string.IsNullOrEmpty(displayName) ? "<unbound>" : displayName;
        }

        if (bindingIcon != null)
        {
            bool hasSprite = InputGlyphService.TryGetUISpriteForActionPath(action, partName, context, out Sprite sprite);
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
        bool hasPending = InputBindingManager.PreparedRebindCount > 0;

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

        return InputActionResolver.TryGetAction(actionName, out InputAction action) ? action : null;
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

        string displayName = InputGlyphService.GetDisplayNameFromControlPath(overridePath);
        return string.IsNullOrEmpty(displayName) ? overridePath : displayName;
    }

    private bool TryGetPendingTargetContext(out InputBindingManager.RebindContext context)
    {
        for (int i = 0; i < InputBindingManager.PreparedRebindCount; i++)
        {
            InputBindingManager.RebindContext candidate = InputBindingManager.PreparedRebinds[i];
            if (IsTargetContext(candidate))
            {
                context = candidate;
                return true;
            }
        }

        context = null;
        return false;
    }

    private bool IsTargetContext(InputBindingManager.RebindContext context)
    {
        if (context == null || context.Action == null)
        {
            return false;
        }

        InputAction targetAction = ResolveAction();
        if (targetAction == null || context.Action.id != targetAction.id)
        {
            return false;
        }

        string partName = NormalizeCompositePartName();
        if (string.IsNullOrEmpty(partName))
        {
            return true;
        }

        if (context.BindingIndex < 0 || context.BindingIndex >= targetAction.bindings.Count)
        {
            return false;
        }

        InputBinding binding = targetAction.bindings[context.BindingIndex];
        return string.Equals(binding.name, partName, StringComparison.OrdinalIgnoreCase);
    }

    private bool ContainsTargetContext(InputBindingManager.RebindContext[] contexts)
    {
        if (contexts == null)
        {
            return false;
        }

        for (int i = 0; i < contexts.Length; i++)
        {
            if (IsTargetContext(contexts[i]))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
