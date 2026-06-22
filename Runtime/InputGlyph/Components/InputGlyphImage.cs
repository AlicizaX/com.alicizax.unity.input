#if INPUTSYSTEM_SUPPORT
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[AddComponentMenu("UI/Input Glyph Image")]
public sealed class InputGlyphImage : InputGlyphBehaviourBase
{
    [Header("Output")] [SerializeField] private Image targetImage;

    private Sprite _cachedSprite;

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
        if (action == null)
        {
            ClearImage();
            return;
        }

        bool hasSprite = UXInput.Glyph.TryGetUISpriteForActionPath(action, CompositePartName, out Sprite sprite);
        if (!hasSprite)
        {
            sprite = null;
        }

        if (_cachedSprite != sprite || targetImage.sprite != sprite)
        {
            _cachedSprite = sprite;
            targetImage.sprite = sprite;
        }
    }

    private void ClearImage()
    {
        _cachedSprite = null;
        if (targetImage != null && targetImage.sprite != null)
        {
            targetImage.sprite = null;
        }
    }
}
#endif
