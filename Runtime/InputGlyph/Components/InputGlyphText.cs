#if INPUTSYSTEM_SUPPORT
using AlicizaX;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[AddComponentMenu("UI/Input Glyph Text")]
public sealed class InputGlyphText : InputGlyphBehaviourBase
{
    [Header("Output")]
    [SerializeField] private TMP_Text targetText;

    private string _templateText;

    protected override void AutoAssignTarget()
    {
        if (targetText == null)
        {
            targetText = GetComponent<TMP_Text>();
        }
    }

    protected override void OnEnable()
    {
        CacheTemplateText();
        base.OnEnable();
    }

    protected override void RefreshGlyph()
    {
        if (targetText == null)
        {
            return;
        }

        CacheTemplateText();

        InputAction action = ResolveAction();
        if (action == null)
        {
            ApplyText(_templateText);
            return;
        }

        string replacementToken = UXInput.Glyph.TryGetTMPTagForActionPath(
            action,
            CompositePartName,
            out string tag,
            out string displayFallback)
            ? tag
            : displayFallback;

        if (string.IsNullOrEmpty(replacementToken))
        {
            ApplyText(_templateText);
            return;
        }

        ApplyText(Utility.Text.Format(_templateText, replacementToken));
    }

    private void CacheTemplateText()
    {
        if (targetText != null && string.IsNullOrEmpty(_templateText))
        {
            _templateText = targetText.text;
        }
    }

    private void ApplyText(string text)
    {
        if (targetText.text != text)
        {
            targetText.text = text;
        }
    }
}
#endif
