#if INPUTSYSTEM_SUPPORT
using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

[AddComponentMenu("UI/Input Glyph Text")]
public sealed class InputGlyphText : InputGlyphBehaviourBase
{
    [Header("Output")]
    [SerializeField] private TMP_Text targetText;

    private string _templateText;
    private string _cachedTemplate;
    private string _cachedToken;
    private string _cachedText;

    protected override void AssignTargetIfMissing()
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

        string replacementToken = UXInput.Glyph.TryGetTag(
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

        ApplyText(BuildDisplayText(_templateText, replacementToken));
    }

    private string BuildDisplayText(string template, string token)
    {
        if (string.Equals(_cachedTemplate, template, StringComparison.Ordinal)
            && string.Equals(_cachedToken, token, StringComparison.Ordinal)
            && _cachedText != null)
        {
            return _cachedText;
        }

        _cachedTemplate = template;
        _cachedToken = token;
        _cachedText = ReplaceFirstPlaceholder(template, token);
        return _cachedText;
    }

    /// <summary>
    /// 只替换第一个 {0}。不调用 string.Format，避免模板中的非法占位符每次刷新抛异常。
    /// </summary>
    private static string ReplaceFirstPlaceholder(string template, string token)
    {
        if (string.IsNullOrEmpty(template))
        {
            return token;
        }

        int placeholderIndex = template.IndexOf("{0}", StringComparison.Ordinal);
        if (placeholderIndex < 0)
        {
            return template;
        }

        return string.Create(
            template.Length - 3 + token.Length,
            new TextReplacementState(template, token, placeholderIndex),
            static (span, state) =>
            {
                state.Template.AsSpan(0, state.PlaceholderIndex).CopyTo(span);
                state.Token.AsSpan().CopyTo(span.Slice(state.PlaceholderIndex));
                int tailStart = state.PlaceholderIndex + 3;
                state.Template.AsSpan(tailStart).CopyTo(span.Slice(state.PlaceholderIndex + state.Token.Length));
            });
    }

    private readonly struct TextReplacementState
    {
        internal readonly string Template;
        internal readonly string Token;
        internal readonly int PlaceholderIndex;

        internal TextReplacementState(string template, string token, int placeholderIndex)
        {
            Template = template;
            Token = token;
            PlaceholderIndex = placeholderIndex;
        }
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
        if (!string.Equals(targetText.text, text, StringComparison.Ordinal))
        {
            targetText.text = text;
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        ValidateTemplate();
    }

    private void ValidateTemplate()
    {
        if (targetText == null)
        {
            return;
        }

        string template = targetText.text;
        if (string.IsNullOrEmpty(template))
        {
            return;
        }

        bool hasPlaceholder = template.IndexOf("{0}", StringComparison.Ordinal) >= 0;
        bool hasBrace = template.IndexOf('{') >= 0;
        if (hasBrace && !hasPlaceholder)
        {
            Debug.LogWarning(
                "Input Glyph Text on '" + name + "' contains '{' but no {0} placeholder. " +
                "The glyph token will not be inserted; use {0} where the key sprite should appear.",
                this);
        }
    }
#endif
}
#endif
