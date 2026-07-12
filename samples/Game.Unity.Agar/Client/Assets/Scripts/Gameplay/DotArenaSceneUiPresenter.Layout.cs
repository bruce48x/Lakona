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
            panelRect.anchorMin = Vector2.one;
            panelRect.anchorMax = Vector2.one;
            panelRect.pivot = Vector2.one;
            panelRect.anchoredPosition = new Vector2(-18f, -18f);
            panelRect.sizeDelta = new Vector2(220f, 246f);

            _matchRankingTitleText = EnsureMatchRankingText(
                _matchRankingPanel.transform,
                "TitleText",
                new Vector2(0f, -18f),
                new Vector2(196f, 28f),
                18f,
                FontStyles.Bold,
                TextAlignmentOptions.Center);
            _matchRankingHeaderText = EnsureMatchRankingText(
                _matchRankingPanel.transform,
                "HeaderText",
                new Vector2(0f, -52f),
                new Vector2(198f, 20f),
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
            rect.anchoredPosition = new Vector2(0f, -80f - (index * 27f));
            rect.sizeDelta = new Vector2(196f, 23f);

            var background = rowObject.GetComponent<Image>();
            if (background == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/MatchRankingPanel/{rowName} Image");
                return null;
            }

            background.raycastTarget = false;

            var rankText = EnsureMatchRankingRowText(rowObject.transform, "RankText", 0f, 34f, TextAlignmentOptions.Left);
            var nameText = EnsureMatchRankingRowText(rowObject.transform, "NameText", 34f, 104f, TextAlignmentOptions.Left);
            var massText = EnsureMatchRankingRowText(rowObject.transform, "MassText", 140f, 52f, TextAlignmentOptions.Right);
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
            DotArenaUiRect.TopCenter(new Vector2(0f, -12f), new Vector2(360f, 58f)).Apply(panelTransform);

            _hudCountdownText = EnsureTopStatusText(
                panelTransform,
                "CountdownText",
                FindSceneUiText("SceneUI/OverlayLayer/CountdownText")
                    ?? FindSceneUiText("SceneUI/CountdownText")
                    ?? FindSceneUiText("SceneUI/HUDPanel/CountdownText"),
                new Vector2(0f, -28f),
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
                _lobbyPanel.SetActive(false);
                return;
            }

            WarnMissingAuthoredSceneUi("SceneUI/LobbyPanel");
        }
    }
}
