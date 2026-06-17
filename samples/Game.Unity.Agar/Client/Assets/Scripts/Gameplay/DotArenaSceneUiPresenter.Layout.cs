#nullable enable

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
        private void EnsureMatchRankingPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _matchRankingPanel = FindSceneUiObject("SceneUI/MatchRankingPanel");
            if (_matchRankingPanel == null)
            {
                WarnMissingAuthoredSceneUi("SceneUI/MatchRankingPanel");
                return;
            }

            EnsureMatchRankingPanelContents();
            _matchRankingPanel.SetActive(false);
        }

        private void EnsureMatchRankingPanelContents()
        {
            if (_matchRankingPanel == null)
            {
                return;
            }

            var panelRect = (RectTransform)_matchRankingPanel.transform;
            panelRect.anchorMin = new Vector2(1f, 0.5f);
            panelRect.anchorMax = new Vector2(1f, 0.5f);
            panelRect.pivot = new Vector2(1f, 0.5f);
            panelRect.anchoredPosition = new Vector2(-18f, 0f);
            panelRect.sizeDelta = new Vector2(260f, 390f);

            _matchRankingTitleText = EnsureMatchRankingText(
                _matchRankingPanel.transform,
                "TitleText",
                new Vector2(0f, -16f),
                new Vector2(220f, 28f),
                18f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            _matchRankingHeaderText = EnsureMatchRankingText(
                _matchRankingPanel.transform,
                "HeaderText",
                new Vector2(0f, -48f),
                new Vector2(224f, 20f),
                12f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);

            _matchRankingRows.Clear();
            for (var i = 0; i < MatchRankingMaxRows; i++)
            {
                var row = EnsureMatchRankingRow(i);
                if (row != null)
                {
                    _matchRankingRows.Add(row);
                }
            }
        }

        private TMP_Text? EnsureMatchRankingText(
            Transform parent,
            string name,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            FontStyles fontStyles,
            TextAlignmentOptions alignment)
        {
            var target = parent.Find(name);
            if (target == null || !target.TryGetComponent<TMP_Text>(out var text))
            {
                WarnMissingAuthoredSceneUi($"{BuildSceneUiPath(parent)}/{name}");
                return null;
            }

            DotArenaUiRect.TopCenter(anchoredPosition, size).Apply(text.rectTransform);
            DotArenaUiStyleCatalog.ApplyText(text, DotArenaUiStyleCatalog.RankingText(fontSize, fontStyles, alignment));
            return text;
        }

        private MatchRankingRowUi? EnsureMatchRankingRow(int index)
        {
            var rowName = $"Row{index + 1}";
            var rowTransform = _matchRankingPanel!.transform.Find(rowName);
            if (rowTransform == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/MatchRankingPanel/{rowName}");
                return null;
            }

            var rowObject = rowTransform.gameObject;
            var rect = (RectTransform)rowObject.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -76f - (index * 29f));
            rect.sizeDelta = new Vector2(224f, 24f);

            var background = rowObject.GetComponent<Image>();
            if (background == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/MatchRankingPanel/{rowName} Image");
                return null;
            }

            background.raycastTarget = false;

            var rankText = EnsureMatchRankingRowText(rowObject.transform, "RankText", 0f, 34f, TextAlignmentOptions.Left);
            var nameText = EnsureMatchRankingRowText(rowObject.transform, "NameText", 36f, 118f, TextAlignmentOptions.Left);
            var massText = EnsureMatchRankingRowText(rowObject.transform, "MassText", 164f, 56f, TextAlignmentOptions.Right);
            if (rankText == null || nameText == null || massText == null)
            {
                return null;
            }

            return new MatchRankingRowUi(rowObject, background, rankText, nameText, massText);
        }

        private TMP_Text? EnsureMatchRankingRowText(Transform parent, string name, float x, float width, TextAlignmentOptions alignment)
        {
            var target = parent.Find(name);
            if (target == null || !target.TryGetComponent<TMP_Text>(out var text))
            {
                WarnMissingAuthoredSceneUi($"{BuildSceneUiPath(parent)}/{name}");
                return null;
            }

            DotArenaUiRect.LeftMiddle(new Vector2(x, 0f), new Vector2(width, 20f)).Apply(text.rectTransform);
            DotArenaUiStyleCatalog.ApplyText(text, DotArenaUiStyleCatalog.RankingText(12f, FontStyles.Bold, alignment));
            return text;
        }

        private void EnsureHudCountdownText()
        {
            var parent = OverlayLayer != null ? OverlayLayer.transform : _sceneUiRoot?.transform;
            if (parent == null)
            {
                return;
            }

            if (_hudCountdownText != null)
            {
                _hudCountdownText.transform.SetParent(parent, false);
                var existingRect = _hudCountdownText.rectTransform;
                existingRect.anchorMin = new Vector2(0.5f, 1f);
                existingRect.anchorMax = new Vector2(0.5f, 1f);
                existingRect.pivot = new Vector2(0.5f, 1f);
                existingRect.anchoredPosition = new Vector2(0f, -10f);
                existingRect.sizeDelta = new Vector2(220f, 28f);
                _hudCountdownText.alignment = TextAlignmentOptions.Center;
                _hudCountdownText.fontSize = 18f;
                _hudCountdownText.fontStyle = FontStyles.Bold;
                _hudCountdownText.color = UiAccentTextColor;
                _hudCountdownText.overflowMode = TextOverflowModes.Ellipsis;
                _hudCountdownText.enableWordWrapping = false;
                _hudCountdownText.richText = false;
                return;
            }

            WarnMissingAuthoredSceneUi($"{BuildSceneUiPath(parent)}/CountdownText");
        }

        private void EnsureDebugPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _debugPanel = FindSceneUiObject("SceneUI/DebugPanel");
            if (_debugPanel != null)
            {
                EnsureDebugPanelContents();
                _debugPanel.SetActive(false);
                return;
            }

            WarnMissingAuthoredSceneUi("SceneUI/DebugPanel");
        }

        private void EnsureDebugPanelContents()
        {
            if (_debugPanel == null)
            {
                return;
            }

            var panelRect = (RectTransform)_debugPanel.transform;
            panelRect.sizeDelta = new Vector2(300f, 170f);

            if (FindSceneUiText("SceneUI/DebugPanel/TitleText") == null)
            {
                WarnMissingAuthoredSceneUi("SceneUI/DebugPanel/TitleText");
            }

            if (FindSceneUiText("SceneUI/DebugPanel/DetailText") == null)
            {
                WarnMissingAuthoredSceneUi("SceneUI/DebugPanel/DetailText");
            }
        }

        private void EnsureMultiplayerLabelLayout()
        {
            FixMultiplayerLabelRect(_accountLabelText, -132f);
            FixMultiplayerLabelRect(_passwordLabelText, -168f);
        }

        private static void FixMultiplayerLabelRect(TMP_Text? label, float y)
        {
            if (label == null)
            {
                return;
            }

            var rect = label.rectTransform;
            var misplaced = rect.anchorMin == new Vector2(0f, 1f) && rect.anchorMax == new Vector2(0f, 1f) && rect.anchoredPosition.x < -100f;
            if (!misplaced)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(-136f, y);
        }

        private void EnsureLobbyPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _lobbyPanel = FindSceneUiObject("SceneUI/LobbyPanel");
            if (_lobbyPanel != null)
            {
                EnsureLobbyPanelContents();
                _lobbyPanel.SetActive(false);
                return;
            }

            WarnMissingAuthoredSceneUi("SceneUI/LobbyPanel");
        }

        private void EnsureLobbyPanelContents()
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            var panelRect = (RectTransform)_lobbyPanel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.offsetMin = new Vector2(36f, 36f);
            panelRect.offsetMax = new Vector2(-36f, -36f);

            EnsureLobbyTextElement("TitleText", new Vector2(0f, -22f), new Vector2(720f, 38f), 24f, FontStyles.Bold, TextAlignmentOptions.Center);
            EnsureLobbyTextElement("SummaryText", new Vector2(0f, -72f), new Vector2(980f, 30f), 14f, FontStyles.Normal, TextAlignmentOptions.Center);
            EnsureLobbyButtonElement("ProfileButton", new Vector2(-300f, -128f), new Vector2(120f, 34f), "资料");
            EnsureLobbyButtonElement("TasksButton", new Vector2(-180f, -128f), new Vector2(110f, 34f), string.Empty);
            EnsureLobbyButtonElement("ShopButton", new Vector2(-60f, -128f), new Vector2(110f, 34f), string.Empty);
            EnsureLobbyButtonElement("RecordsButton", new Vector2(60f, -128f), new Vector2(110f, 34f), string.Empty);
            EnsureLobbyButtonElement("LeaderboardButton", new Vector2(190f, -128f), new Vector2(130f, 34f), "排行榜");
            EnsureLobbyButtonElement("SettingsButton", new Vector2(330f, -128f), new Vector2(130f, 34f), "设置");
            EnsureLobbyTextElement("HighlightsText", new Vector2(0f, -184f), new Vector2(980f, 56f), 14f, FontStyles.Bold, TextAlignmentOptions.Center);
            EnsureLobbyTextElement("QuickActionsText", new Vector2(-410f, -250f), new Vector2(220f, 28f), 13f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            EnsureLobbyButtonElement("QuickActionButton1", new Vector2(-220f, -246f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton2", new Vector2(-20f, -246f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton3", new Vector2(180f, -246f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton4", new Vector2(380f, -246f), new Vector2(180f, 40f), "Action");
            EnsureLobbyTextElement("DetailText", new Vector2(0f, -326f), new Vector2(980f, 290f), 14f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            EnsureLobbyButtonElement("PrimaryActionButton", new Vector2(-120f, -650f), new Vector2(220f, 42f), "Action");
            EnsureLobbyButtonElement("SecondaryActionButton", new Vector2(120f, -650f), new Vector2(220f, 42f), "Action");
            EnsureLobbyTextElement("FooterText", new Vector2(0f, -708f), new Vector2(980f, 24f), 12f, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        private void EnsureLobbyTextElement(string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles, TextAlignmentOptions alignment)
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            var text = FindSceneUiText($"SceneUI/LobbyPanel/{name}");
            if (text == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/LobbyPanel/{name}");
                return;
            }

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            text.fontSize = fontSize;
            text.fontStyle = fontStyles;
            text.alignment = alignment;
        }

        private void EnsureLobbyButtonElement(string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            var button = FindSceneUiButton($"SceneUI/LobbyPanel/{name}");
            if (button == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/LobbyPanel/{name}");
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var text = FindSceneUiText($"SceneUI/LobbyPanel/{name}/Label");
            if (text != null)
            {
                StretchButtonLabel(text.rectTransform);
                text.text = label;
            }
        }
    }
}
