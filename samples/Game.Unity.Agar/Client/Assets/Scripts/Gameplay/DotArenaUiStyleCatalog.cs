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
                image.sprite = panelSprite;
                image.type = panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.color = color;
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

        public static void ApplyButton(Button? button, Sprite? buttonSprite)
        {
            if (button == null)
            {
                return;
            }

            _ = buttonSprite;
            if (button.TryGetComponent<Image>(out var image))
            {
                image.enabled = false;
                Object.Destroy(image);
            }

            var gradient = button.GetComponent<DotArenaGradientGraphic>();
            if (gradient == null)
            {
                gradient = button.gameObject.AddComponent<DotArenaGradientGraphic>();
            }

            gradient.TopLeft = new Color(0.08f, 0.82f, 0.86f, 1f);
            gradient.TopRight = new Color(0.25f, 0.91f, 0.72f, 1f);
            gradient.BottomLeft = new Color(0f, 0.52f, 0.66f, 1f);
            gradient.BottomRight = new Color(0.02f, 0.68f, 0.72f, 1f);
            gradient.CornerRadius = 18f;
            gradient.raycastTarget = true;
            button.targetGraphic = gradient;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.82f, 0.88f, 0.92f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.64f, 0.76f, 0.8f, 0.72f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.transition = Selectable.Transition.ColorTint;
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
