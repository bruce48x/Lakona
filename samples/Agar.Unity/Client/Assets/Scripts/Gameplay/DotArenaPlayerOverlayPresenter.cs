#nullable enable

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaPlayerOverlayPresenter
    {
        private readonly Dictionary<string, PlayerOverlayView> _views = new(StringComparer.Ordinal);

        public Dictionary<string, PlayerOverlayView> Views => _views;

        public void EnsureOverlay(DotArenaSceneUiPresenter sceneUiPresenter, string playerId)
        {
            var overlayLayer = sceneUiPresenter.OverlayLayer;
            if (overlayLayer == null || _views.ContainsKey(playerId))
            {
                return;
            }

            var root = new GameObject($"{playerId}Overlay", typeof(RectTransform));
            root.transform.SetParent(overlayLayer, false);

            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(140f, 40f);

            var nameObject = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameObject.transform.SetParent(root.transform, false);
            var nameRect = (RectTransform)nameObject.transform;
            nameRect.anchorMin = new Vector2(0.5f, 0.5f);
            nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.anchoredPosition = new Vector2(0f, -10f);
            nameRect.sizeDelta = new Vector2(140f, 20f);

            var nameText = nameObject.GetComponent<TextMeshProUGUI>();
            nameText.font = ResolveOverlayFontAsset();
            nameText.fontSize = 16;
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.color = UiPrimaryTextColor;

            var massObject = new GameObject("MassText", typeof(RectTransform), typeof(TextMeshProUGUI));
            massObject.transform.SetParent(root.transform, false);
            var massRect = (RectTransform)massObject.transform;
            massRect.anchorMin = new Vector2(0.5f, 0.5f);
            massRect.anchorMax = new Vector2(0.5f, 0.5f);
            massRect.pivot = new Vector2(0.5f, 0.5f);
            massRect.anchoredPosition = new Vector2(0f, 8f);
            massRect.sizeDelta = new Vector2(140f, 18f);

            var massText = massObject.GetComponent<TextMeshProUGUI>();
            massText.font = ResolveOverlayFontAsset();
            massText.fontSize = 14;
            massText.fontStyle = FontStyles.Bold;
            massText.alignment = TextAlignmentOptions.Center;
            massText.enableWordWrapping = false;
            massText.overflowMode = TextOverflowModes.Ellipsis;
            massText.color = UiAccentTextColor;

            _views.Add(playerId, new PlayerOverlayView(root, rootRect, nameText, massText));
        }

        public void UpdateOverlayViews(
            DotArenaSceneUiPresenter sceneUiPresenter,
            IReadOnlyDictionary<string, DotView> worldViews,
            IReadOnlyDictionary<string, PlayerRenderState> renderStates)
        {
            if (sceneUiPresenter.OverlayLayer == null)
            {
                return;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                foreach (var overlay in _views.Values)
                {
                    overlay.Root.SetActive(false);
                }

                return;
            }

            var pixelsPerWorldUnit = Screen.height / (camera.orthographicSize * 2f);

            foreach (var entry in _views)
            {
                if (!worldViews.TryGetValue(entry.Key, out var view) ||
                    !renderStates.TryGetValue(entry.Key, out var renderState))
                {
                    entry.Value.Root.SetActive(false);
                    continue;
                }

                var screenPosition = camera.WorldToScreenPoint(view.Root.transform.position);
                if (screenPosition.z <= 0f)
                {
                    entry.Value.Root.SetActive(false);
                    continue;
                }

                entry.Value.Root.SetActive(true);
                var serverRadius = !float.IsNaN(renderState.Radius) && !float.IsInfinity(renderState.Radius) && renderState.Radius > 0f
                    ? renderState.Radius
                    : GameplayConfig.PlayerVisualRadius;
                var diameterPixels = serverRadius * 2f * pixelsPerWorldUnit;
                var labelWidth = Mathf.Max(96f, diameterPixels * 2f);
                var nameHeight = Mathf.Max(18f, diameterPixels * 0.36f);
                var massHeight = Mathf.Max(16f, diameterPixels * 0.3f);

                entry.Value.RootRect.anchoredPosition = new Vector2(screenPosition.x, screenPosition.y);
                entry.Value.RootRect.sizeDelta = new Vector2(labelWidth, nameHeight + massHeight + 4f);

                var nameRect = entry.Value.NameText.rectTransform;
                nameRect.sizeDelta = new Vector2(labelWidth, nameHeight);
                nameRect.anchoredPosition = new Vector2(0f, nameHeight * 0.55f);
                entry.Value.NameText.fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.24f, 14f, 22f));

                var massRect = entry.Value.MassText.rectTransform;
                massRect.sizeDelta = new Vector2(labelWidth, massHeight);
                massRect.anchoredPosition = new Vector2(0f, -(massHeight * 0.55f));
                entry.Value.MassText.fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.22f, 13f, 20f));
            }
        }

        public void Clear(Action<UnityEngine.Object> destroyObject)
        {
            foreach (var overlay in _views.Values)
            {
                destroyObject(overlay.Root);
            }

            _views.Clear();
        }

        private static TMP_FontAsset? ResolveOverlayFontAsset()
        {
            var projectFont = Resources.Load<TMP_FontAsset>(TmpFallbackFontAssetResourcePath);
            if (projectFont != null)
            {
                return projectFont;
            }

            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }
    }
}
