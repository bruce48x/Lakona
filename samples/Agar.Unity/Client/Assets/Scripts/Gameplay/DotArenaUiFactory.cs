#nullable enable

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaUiFactory
    {
        private readonly Func<TMP_FontAsset?> _fontProvider;

        public DotArenaUiFactory(Func<TMP_FontAsset?> fontProvider)
        {
            _fontProvider = fontProvider;
        }

        public static GameObject CreatePanel(Transform parent, string name)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            return panel;
        }

        public TMP_Text CreateText(
            Transform parent,
            string name,
            DotArenaUiRect rect,
            DotArenaUiTextStyle style)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.font = _fontProvider();
            ApplyText(text, rect, style);
            return text;
        }

        public TMP_Text EnsureText(
            Transform parent,
            string name,
            DotArenaUiRect rect,
            DotArenaUiTextStyle style)
        {
            var existing = parent.Find(name);
            if (existing != null && existing.TryGetComponent<TMP_Text>(out var text))
            {
                ApplyText(text, rect, style);
                return text;
            }

            return CreateText(parent, name, rect, style);
        }

        public Button CreateButton(
            Transform parent,
            string name,
            DotArenaUiRect rect,
            string label)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            rect.Apply((RectTransform)buttonObject.transform);

            CreateButtonLabel(buttonObject.transform, label);
            return buttonObject.GetComponent<Button>();
        }

        public Button EnsureButton(
            Transform parent,
            string name,
            DotArenaUiRect rect,
            string label)
        {
            var existing = parent.Find(name);
            if (existing != null && existing.TryGetComponent<Button>(out var button))
            {
                rect.Apply(button.GetComponent<RectTransform>());
                SetButtonLabel(button.transform, label);
                return button;
            }

            return CreateButton(parent, name, rect, label);
        }

        public TMP_Text CreateButtonLabel(Transform parent, string label)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);
            DotArenaUiRect.Stretch.Apply((RectTransform)labelObject.transform);

            var text = labelObject.GetComponent<TextMeshProUGUI>();
            text.font = _fontProvider();
            DotArenaUiStyleCatalog.ApplyText(text, DotArenaUiStyleCatalog.ButtonLabelText);
            text.text = label;
            return text;
        }

        public static void SetButtonLabel(Transform button, string label)
        {
            if (button.Find("Label") is not RectTransform labelRect)
            {
                return;
            }

            DotArenaUiRect.Stretch.Apply(labelRect);
            if (labelRect.TryGetComponent<TMP_Text>(out var text))
            {
                text.text = label;
            }
        }

        private static void ApplyText(TMP_Text text, DotArenaUiRect rect, DotArenaUiTextStyle style)
        {
            rect.Apply(text.rectTransform);
            DotArenaUiStyleCatalog.ApplyText(text, style);
        }
    }

    internal readonly struct DotArenaUiRect
    {
        public DotArenaUiRect(
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Vector2 offsetMin,
            Vector2 offsetMax,
            bool useOffsets)
        {
            AnchorMin = anchorMin;
            AnchorMax = anchorMax;
            Pivot = pivot;
            AnchoredPosition = anchoredPosition;
            SizeDelta = sizeDelta;
            OffsetMin = offsetMin;
            OffsetMax = offsetMax;
            UseOffsets = useOffsets;
        }

        public Vector2 AnchorMin { get; }
        public Vector2 AnchorMax { get; }
        public Vector2 Pivot { get; }
        public Vector2 AnchoredPosition { get; }
        public Vector2 SizeDelta { get; }
        public Vector2 OffsetMin { get; }
        public Vector2 OffsetMax { get; }
        public bool UseOffsets { get; }

        public static DotArenaUiRect Stretch => new(
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            Vector2.zero,
            true);

        public static DotArenaUiRect Center(Vector2 size) => new(
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            size,
            Vector2.zero,
            Vector2.zero,
            false);

        public static DotArenaUiRect TopCenter(Vector2 anchoredPosition, Vector2 size) => new(
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            anchoredPosition,
            size,
            Vector2.zero,
            Vector2.zero,
            false);

        public static DotArenaUiRect TopRight(Vector2 anchoredPosition, Vector2 size) => new(
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 1f),
            anchoredPosition,
            size,
            Vector2.zero,
            Vector2.zero,
            false);

        public static DotArenaUiRect LeftMiddle(Vector2 anchoredPosition, Vector2 size) => new(
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            new Vector2(0f, 0.5f),
            anchoredPosition,
            size,
            Vector2.zero,
            Vector2.zero,
            false);

        public static DotArenaUiRect Fill(Vector2 offsetMin, Vector2 offsetMax) => new(
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            offsetMin,
            offsetMax,
            true);

        public void Apply(RectTransform rect)
        {
            rect.anchorMin = AnchorMin;
            rect.anchorMax = AnchorMax;
            rect.pivot = Pivot;
            if (UseOffsets)
            {
                rect.offsetMin = OffsetMin;
                rect.offsetMax = OffsetMax;
            }
            else
            {
                rect.anchoredPosition = AnchoredPosition;
                rect.sizeDelta = SizeDelta;
            }
        }
    }

    internal readonly struct DotArenaUiTextStyle
    {
        public DotArenaUiTextStyle(
            float fontSize,
            FontStyles fontStyle,
            TextAlignmentOptions alignment,
            bool wrap,
            TextOverflowModes overflowMode)
        {
            FontSize = fontSize;
            FontStyle = fontStyle;
            Alignment = alignment;
            Wrap = wrap;
            OverflowMode = overflowMode;
        }

        public float FontSize { get; }
        public FontStyles FontStyle { get; }
        public TextAlignmentOptions Alignment { get; }
        public bool Wrap { get; }
        public TextOverflowModes OverflowMode { get; }
    }
}
