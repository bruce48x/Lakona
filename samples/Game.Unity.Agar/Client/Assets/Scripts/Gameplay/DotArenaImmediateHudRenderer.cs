#nullable enable

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal struct DotArenaImmediateHudSnapshot
    {
        public string Status { get; set; }
        public string LocalPlayerId { get; set; }
        public string Account { get; set; }
        public string LocalPlayerMassText { get; set; }
        public int LocalWinCount { get; set; }
        public int LastWorldTick { get; set; }
        public string LocalPlayerBuffText { get; set; }
        public SessionMode SessionMode { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Path { get; set; }
        public string EventMessage { get; set; }
    }

    internal static class DotArenaImmediateHudRenderer
    {
        public static void DrawSessionHud(
            in DotArenaImmediateHudSnapshot snapshot,
            IReadOnlyDictionary<string, DotView> views,
            IReadOnlyDictionary<string, PlayerRenderState> renderStates)
        {
            const float width = 400f;
            const float height = 128f;

            var boxRect = new Rect(16f, 16f, width, height);
            var contentRect = new Rect(28f, 24f, width - 24f, height - 16f);

            var previousColor = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.9f);
            GUI.Box(boxRect, GUIContent.none);
            GUI.color = previousColor;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.86f, 0.91f, 0.96f, 1f) }
            };

            GUI.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 24f), "点阵竞技场", titleStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 24f, contentRect.width, 18f), $"状态: {snapshot.Status}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 44f, contentRect.width, 18f),
                $"玩家: {(snapshot.LocalPlayerId.Length > 0 ? snapshot.LocalPlayerId : snapshot.Account)}   质量: {snapshot.LocalPlayerMassText}   胜场: {snapshot.LocalWinCount}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 64f, contentRect.width, 18f),
                $"场上人数: {views.Count}   状态: {snapshot.LocalPlayerBuffText}", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 84f, contentRect.width, 18f),
                snapshot.SessionMode == SessionMode.SinglePlayer
                    ? "模式: 本地单机"
                    : "模式: 联机对局", bodyStyle);
            GUI.Label(new Rect(contentRect.x, contentRect.y + 104f, contentRect.width, 18f),
                $"事件: {snapshot.EventMessage}", bodyStyle);

            DrawPlayerOverlays(views, renderStates);
        }

        private static void DrawPlayerOverlays(
            IReadOnlyDictionary<string, DotView> views,
            IReadOnlyDictionary<string, PlayerRenderState> renderStates)
        {
            var camera = Camera.main;
            if (camera == null || views.Count == 0)
            {
                return;
            }

            var pixelsPerWorldUnit = Screen.height / (camera.orthographicSize * 2f);

            foreach (var entry in views)
            {
                if (!renderStates.TryGetValue(entry.Key, out var renderState))
                {
                    continue;
                }

                var worldPosition = entry.Value.Root.transform.position;
                var screenPosition = camera.WorldToScreenPoint(worldPosition);
                if (screenPosition.z <= 0f)
                {
                    continue;
                }

                var serverRadius = !float.IsNaN(renderState.Radius) && !float.IsInfinity(renderState.Radius) && renderState.Radius > 0f
                    ? renderState.Radius
                    : GameplayConfig.PlayerVisualRadius;
                var diameterPixels = serverRadius * 2f * pixelsPerWorldUnit;
                var labelWidth = Mathf.Max(96f, diameterPixels * 2f);
                var nameHeight = Mathf.Max(18f, diameterPixels * 0.36f);
                var massHeight = Mathf.Max(16f, diameterPixels * 0.3f);

                var nameStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.24f, 14f, 22f)),
                    clipping = TextClipping.Overflow,
                    normal = { textColor = new Color(0.94f, 0.97f, 1f, 1f) }
                };

                var massStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = Mathf.RoundToInt(Mathf.Clamp(diameterPixels * 0.22f, 13f, 20f)),
                    clipping = TextClipping.Overflow,
                    normal = { textColor = new Color(1f, 0.97f, 0.78f, 1f) }
                };

                var centerX = screenPosition.x;
                var centerY = Screen.height - screenPosition.y;
                var nameRect = new Rect(centerX - (labelWidth * 0.5f), centerY - (nameHeight * 1.05f), labelWidth, nameHeight);
                var massRect = new Rect(centerX - (labelWidth * 0.5f), centerY + (massHeight * 0.05f), labelWidth, massHeight);

                GUI.Label(nameRect, entry.Key, nameStyle);
                GUI.Label(massRect, $"质量: {DotArenaPresentation.FormatMass(renderState.Mass)}", massStyle);
            }
        }
    }
}
