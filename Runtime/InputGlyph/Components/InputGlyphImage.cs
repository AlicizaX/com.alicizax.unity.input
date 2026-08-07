#if INPUTSYSTEM_SUPPORT
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[AddComponentMenu("UI/Input Glyph Image")]
public sealed class InputGlyphImage : InputGlyphBehaviourBase
{
    [Header("Output")]
    [SerializeField] private Image targetImage;

    protected override void AutoAssignTarget()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
        }
    }

    protected override void RefreshGlyph()
    {
        if (targetImage == null)
        {
            return;
        }

        InputAction action = ResolveAction();
        Sprite sprite = null;
        if (action != null)
        {
            UXInput.Glyph.TryGetUISpriteForActionPath(action, CompositePartName, out sprite);
        }

        if (targetImage.sprite != sprite)
        {
            targetImage.sprite = sprite;
        }
    }
}
#endif
