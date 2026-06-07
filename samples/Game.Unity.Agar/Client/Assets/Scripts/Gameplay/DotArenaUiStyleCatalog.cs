#nullable enable

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal static class DotArenaUiStyleCatalog
    {
        public static DotArenaUiTextStyle ButtonLabelText => new(
            13f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            false,
            TextOverflowModes.Ellipsis);

        public static DotArenaUiTextStyle CenterPanelText(float fontSize, FontStyles fontStyle) => new(
            fontSize,
            fontStyle,
            TextAlignmentOptions.Center,
            true,
            TextOverflowModes.Overflow);

        public static DotArenaUiTextStyle LobbyText(float fontSize, FontStyles fontStyle, TextAlignmentOptions alignment) => new(
            fontSize,
            fontStyle,
            alignment,
            true,
            TextOverflowModes.Overflow);

        public static DotArenaUiTextStyle RankingText(float fontSize, FontStyles fontStyle, TextAlignmentOptions alignment) => new(
            fontSize,
            fontStyle,
            alignment,
            false,
            TextOverflowModes.Ellipsis);

        public static void ApplyPanelImage(GameObject? panel, Color color, Sprite? panelSprite)
        {
            if (panel == null)
            {
                return;
            }

            if (panel.TryGetComponent<Image>(out var image))
            {
                if (panelSprite != null && color.a > 0f)
                {
                    image.sprite = panelSprite;
                    image.type = Image.Type.Simple;
                    image.color = Color.white;
                }
                else
                {
                    image.sprite = null;
                    image.color = color;
                }

                image.raycastTarget = color.a > 0f;
            }
        }

        public static void ApplyPanelSprite(GameObject? panel, Sprite? panelSprite, Color tint, bool raycastTarget)
        {
            if (panel == null || !panel.TryGetComponent<Image>(out var image))
            {
                return;
            }

            image.sprite = panelSprite;
            image.type = panelSprite != null && panelSprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : Image.Type.Simple;
            image.color = tint;
            image.raycastTarget = raycastTarget;
        }

        public static void ApplyText(TMP_Text? text, Color color, float fontSize, bool wrap, TextAlignmentOptions alignment, TextOverflowModes overflowMode)
        {
            if (text == null)
            {
                return;
            }

            text.color = color;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.enableWordWrapping = wrap;
            text.overflowMode = overflowMode;
            text.richText = false;
        }

        public static void ApplyText(TMP_Text? text, DotArenaUiTextStyle style)
        {
            if (text == null)
            {
                return;
            }

            text.fontSize = style.FontSize;
            text.fontStyle = style.FontStyle;
            text.alignment = style.Alignment;
            text.enableWordWrapping = style.Wrap;
            text.overflowMode = style.OverflowMode;
            text.richText = false;
        }

        public static void ApplyText(TMP_Text? text, Color color, DotArenaUiTextStyle style)
        {
            if (text == null)
            {
                return;
            }

            text.color = color;
            ApplyText(text, style);
        }

        public static void ApplyButton(Button? button, Sprite? normalSprite, Sprite? pressedSprite)
        {
            if (button == null)
            {
                return;
            }

            if (button.targetGraphic is Image buttonImage && normalSprite != null)
            {
                buttonImage.sprite = normalSprite;
                buttonImage.type = Image.Type.Simple;
                buttonImage.color = Color.white;
            }

            var colors = button.colors;
            colors.normalColor = normalSprite != null ? Color.white : new Color(0.2f, 0.29f, 0.38f, 1f);
            colors.highlightedColor = normalSprite != null ? new Color(1.08f, 1.08f, 1.08f, 1f) : new Color(0.27f, 0.39f, 0.5f, 1f);
            colors.pressedColor = normalSprite != null ? new Color(0.86f, 0.9f, 0.94f, 1f) : new Color(0.14f, 0.22f, 0.3f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.2f, 0.2f, 0.22f, 0.7f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            if (pressedSprite != null)
            {
                button.transition = Selectable.Transition.SpriteSwap;
                var spriteState = button.spriteState;
                spriteState.highlightedSprite = normalSprite;
                spriteState.pressedSprite = pressedSprite;
                spriteState.selectedSprite = normalSprite;
                spriteState.disabledSprite = normalSprite;
                button.spriteState = spriteState;
            }
            else
            {
                button.transition = Selectable.Transition.ColorTint;
            }
        }

        public static void ApplyInputField(TMP_InputField? inputField)
        {
            if (inputField == null)
            {
                return;
            }

            if (inputField.targetGraphic is Image inputImage)
            {
                inputImage.color = UiInputBackgroundColor;
            }

            ApplyText(inputField.textComponent, UiPrimaryTextColor, 14f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);

            if (inputField.placeholder is TMP_Text placeholderText)
            {
                ApplyText(placeholderText, UiMutedTextColor, 13f, false, TextAlignmentOptions.MidlineLeft, TextOverflowModes.Ellipsis);
            }
        }
    }
}
