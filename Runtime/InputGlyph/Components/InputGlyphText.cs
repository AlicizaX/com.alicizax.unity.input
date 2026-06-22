#if INPUTSYSTEM_SUPPORT
using AlicizaX;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[AddComponentMenu("UI/Input Glyph Text")]
public sealed class InputGlyphText : InputGlyphBehaviourBase
{
    [Header("Output")] [SerializeField] private TMP_Text targetText;

    private string _templateText;
    private string _cachedFormattedText;
    private string _cachedReplacementToken;

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
            ResetText();
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
            ResetText();
            return;
        }

        if (_cachedReplacementToken == replacementToken
            && !string.IsNullOrEmpty(_cachedFormattedText)
            && targetText.text == _cachedFormattedText)
        {
            return;
        }

        string formattedText = Utility.Text.Format(_templateText, replacementToken);
        if (_cachedFormattedText == formattedText && targetText.text == formattedText)
        {
            _cachedReplacementToken = replacementToken;
            return;
        }

        _cachedReplacementToken = replacementToken;
        if (_cachedFormattedText != formattedText || targetText.text != formattedText)
        {
            _cachedFormattedText = formattedText;
            targetText.text = formattedText;
        }
    }

    private void CacheTemplateText()
    {
        if (targetText == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(_templateText))
        {
            _templateText = targetText.text;
        }
    }

    private void ResetText()
    {
        _cachedReplacementToken = null;
        _cachedFormattedText = null;
        if (targetText != null && targetText.text != _templateText)
        {
            targetText.text = _templateText;
        }
    }
}
#endif
