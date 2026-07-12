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
            rootRect.sizeDelta = new Vector2(128f, 24f);

            var nameObject = new GameObject("NameText", typeof(RectTransform), typeof(TextMeshProUGUI));
            nameObject.transform.SetParent(root.transform, false);
            var nameRect = (RectTransform)nameObject.transform;
            nameRect.anchorMin = new Vector2(0.5f, 0.5f);
            nameRect.anchorMax = new Vector2(0.5f, 0.5f);
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.anchoredPosition = Vector2.zero;
            nameRect.sizeDelta = new Vector2(128f, 22f);

            var nameText = nameObject.GetComponent<TextMeshProUGUI>();
            nameText.font = ResolveOverlayFontAsset();
            nameText.fontSize = 14;
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.color = UiPrimaryTextColor;

            _views.Add(playerId, new PlayerOverlayView(root, rootRect, nameText));
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

            var canvas = sceneUiPresenter.OverlayLayer.GetComponentInParent<Canvas>();
            var uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

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
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        sceneUiPresenter.OverlayLayer,
                        screenPosition,
                        uiCamera,
                        out var localPoint))
                {
                    entry.Value.Root.SetActive(false);
                    continue;
                }

                const float labelWidth = 128f;
                const float nameHeight = 22f;
                entry.Value.RootRect.anchoredPosition = localPoint + new Vector2(0f, 26f);
                entry.Value.RootRect.sizeDelta = new Vector2(labelWidth, nameHeight);

                var nameRect = entry.Value.NameText.rectTransform;
                nameRect.sizeDelta = new Vector2(labelWidth, nameHeight);
                nameRect.anchoredPosition = Vector2.zero;
                entry.Value.NameText.fontSize = 14f;
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
