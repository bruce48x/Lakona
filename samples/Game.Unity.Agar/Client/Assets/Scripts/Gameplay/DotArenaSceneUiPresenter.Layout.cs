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
            panelRect.sizeDelta = new Vector2(246f, 410f);

            _matchRankingTitleText = EnsureMatchRankingText(
                _matchRankingPanel.transform,
                "TitleText",
                new Vector2(0f, -18f),
                new Vector2(220f, 28f),
                18f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            _matchRankingHeaderText = EnsureMatchRankingText(
                _matchRankingPanel.transform,
                "HeaderText",
                new Vector2(0f, -52f),
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
            rect.anchoredPosition = new Vector2(0f, -84f - (index * 30f));
            rect.sizeDelta = new Vector2(216f, 25f);

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

            DotArenaUiRect.LeftMiddle(new Vector2(x, 0f), new Vector2(width, 21f)).Apply(text.rectTransform);
            DotArenaUiStyleCatalog.ApplyText(text, DotArenaUiStyleCatalog.RankingText(12f, FontStyles.Bold, alignment));
            return text;
        }

        private void EnsureTopStatusPanel()
        {
            var parent = OverlayLayer != null ? OverlayLayer.transform : _sceneUiRoot?.transform;
            if (parent == null)
            {
                return;
            }

            var panelTransform = parent.Find("TopStatusPanel") as RectTransform;
            if (panelTransform == null)
            {
                var panelObject = new GameObject("TopStatusPanel", typeof(RectTransform));
                panelObject.transform.SetParent(parent, false);
                panelTransform = (RectTransform)panelObject.transform;
            }

            _topStatusPanel = panelTransform.gameObject;
            DotArenaUiRect.TopCenter(new Vector2(0f, -12f), new Vector2(360f, 84f)).Apply(panelTransform);

            _hudStatusText = EnsureTopStatusText(
                panelTransform,
                "StatusText",
                FindSceneUiText("SceneUI/HUDPanel/StatusText"),
                new Vector2(0f, 0f),
                new Vector2(330f, 22f),
                13f,
                FontStyles.Bold,
                UiPrimaryTextColor);
            _hudPlayerText = EnsureTopStatusText(
                panelTransform,
                "PlayerText",
                FindSceneUiText("SceneUI/HUDPanel/PlayerText"),
                new Vector2(0f, -23f),
                new Vector2(330f, 22f),
                13f,
                FontStyles.Bold,
                UiSecondaryTextColor);
            _hudCountdownText = EnsureTopStatusText(
                panelTransform,
                "CountdownText",
                FindSceneUiText("SceneUI/OverlayLayer/CountdownText")
                    ?? FindSceneUiText("SceneUI/CountdownText")
                    ?? FindSceneUiText("SceneUI/HUDPanel/CountdownText"),
                new Vector2(0f, -50f),
                new Vector2(330f, 28f),
                18f,
                FontStyles.Bold,
                UiAccentTextColor);
        }

        private TMP_Text? EnsureTopStatusText(
            RectTransform parent,
            string name,
            TMP_Text? fallback,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize,
            FontStyles fontStyles,
            Color color)
        {
            var target = parent.Find(name);
            var text = target != null ? target.GetComponent<TMP_Text>() : null;
            if (text == null)
            {
                text = fallback;
            }

            if (text == null)
            {
                text = UiFactory.CreateText(
                    parent,
                    name,
                    DotArenaUiRect.TopCenter(anchoredPosition, size),
                    new DotArenaUiTextStyle(fontSize, fontStyles, TextAlignmentOptions.Center, false, TextOverflowModes.Ellipsis));
            }
            else
            {
                text.name = name;
                text.transform.SetParent(parent, false);
                DotArenaUiRect.TopCenter(anchoredPosition, size).Apply(text.rectTransform);
            }

            DotArenaUiStyleCatalog.ApplyText(
                text,
                color,
                fontSize,
                false,
                TextAlignmentOptions.Center,
                TextOverflowModes.Ellipsis);
            text.richText = false;
            text.raycastTarget = false;
            return text;
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
            panelRect.offsetMin = new Vector2(54f, 42f);
            panelRect.offsetMax = new Vector2(-54f, -42f);

            EnsureLobbyTextElement("TitleText", new Vector2(-360f, -34f), new Vector2(360f, 42f), 28f, FontStyles.Bold, TextAlignmentOptions.Left);
            EnsureLobbyTextElement("SummaryText", new Vector2(-360f, -84f), new Vector2(430f, 48f), 14f, FontStyles.Normal, TextAlignmentOptions.Left);
            EnsureLobbyButtonElement("ProfileButton", new Vector2(-374f, -154f), new Vector2(122f, 36f), "Profile");
            EnsureLobbyButtonElement("TasksButton", new Vector2(-242f, -154f), new Vector2(110f, 36f), string.Empty);
            EnsureLobbyButtonElement("ShopButton", new Vector2(-120f, -154f), new Vector2(110f, 36f), string.Empty);
            EnsureLobbyButtonElement("RecordsButton", new Vector2(0f, -154f), new Vector2(110f, 36f), string.Empty);
            EnsureLobbyButtonElement("LeaderboardButton", new Vector2(-238f, -202f), new Vector2(150f, 36f), "Leaderboard");
            EnsureLobbyButtonElement("SettingsButton", new Vector2(-74f, -202f), new Vector2(128f, 36f), "Settings");
            EnsureLobbyTextElement("HighlightsText", new Vector2(230f, -42f), new Vector2(430f, 72f), 15f, FontStyles.Bold, TextAlignmentOptions.Center);
            EnsureLobbyTextElement("QuickActionsText", new Vector2(86f, -148f), new Vector2(220f, 28f), 13f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            EnsureLobbyButtonElement("QuickActionButton1", new Vector2(248f, -146f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton2", new Vector2(448f, -146f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton3", new Vector2(248f, -198f), new Vector2(180f, 40f), "Action");
            EnsureLobbyButtonElement("QuickActionButton4", new Vector2(448f, -198f), new Vector2(180f, 40f), "Action");
            EnsureLobbyTextElement("DetailText", new Vector2(0f, -272f), new Vector2(980f, 250f), 14f, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            EnsureLobbyButtonElement("PrimaryActionButton", new Vector2(-120f, -590f), new Vector2(220f, 44f), "Action");
            EnsureLobbyButtonElement("SecondaryActionButton", new Vector2(120f, -590f), new Vector2(220f, 44f), "Action");
            EnsureLobbyTextElement("FooterText", new Vector2(0f, -646f), new Vector2(980f, 24f), 12f, FontStyles.Normal, TextAlignmentOptions.Center);
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
