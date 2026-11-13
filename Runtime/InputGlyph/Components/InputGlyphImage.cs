#if INPUTSYSTEM_SUPPORT
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AlicizaX.Input
{
    [AddComponentMenu("UI/Input Glyph Image")]
    public sealed class InputGlyphImage : InputGlyphBehaviourBase
    {
        [Header("Output")]
        [SerializeField] private Image targetImage;

        protected override void AssignTargetIfMissing()
        {
            if (targetImage == null)
            {
                targetImage = GetComponent<Image>();
            }
        }

        protected override void RefreshGlyph()
        {
            if (targetImage == null || Glyph == null)
            {
                return;
            }

            InputAction action = ResolveAction();
            Sprite sprite = null;
            if (action != null)
            {
                Glyph.TryGetSprite(action, CompositePartName, out sprite);
            }

            if (targetImage.sprite != sprite)
            {
                targetImage.sprite = sprite;
            }
        }
    }
}
#endif
