#if INPUTSYSTEM_SUPPORT
using UnityEngine;

public abstract class InputGlyphBehaviourBase : MonoBehaviour
{
    protected InputGlyphContext CurrentContext { get; private set; }
    protected string CurrentProfileId
    {
        get
        {
            InputGlyphContext context = CurrentContext;
            return context != null ? context.ProfileId : InputGlyphProfileIds.KeyboardMouse;
        }
    }

    protected virtual void OnEnable()
    {
        CurrentContext = ResolveCurrentContext();
        InputDeviceWatcher.OnDeviceContextChanged += HandleGlobalContextChanged;
        InputBindingManager.BindingsChanged += HandleBindingsChanged;
        RefreshGlyph();
    }

    protected virtual void OnDisable()
    {
        InputDeviceWatcher.OnDeviceContextChanged -= HandleGlobalContextChanged;
        InputBindingManager.BindingsChanged -= HandleBindingsChanged;
    }

    protected InputGlyphContext ResolveCurrentContext()
    {
        return InputDeviceWatcher.CurrentContext;
    }

    private void HandleGlobalContextChanged(InputGlyphContext context)
    {
        ApplyContext(context);
    }

    private void HandleBindingsChanged()
    {
        RefreshGlyph();
    }

    private void ApplyContext(InputGlyphContext context)
    {
        InputGlyphContext previousContext = CurrentContext;
        InputGlyphContext resolvedContext = context != null ? context : InputDeviceWatcher.CurrentContext;
        CurrentContext = resolvedContext;
        OnInputContextChanged(previousContext, resolvedContext);
        RefreshGlyph();
    }

    protected virtual void OnInputContextChanged(InputGlyphContext previousContext, InputGlyphContext newContext)
    {
    }

    protected abstract void RefreshGlyph();
}
#endif
