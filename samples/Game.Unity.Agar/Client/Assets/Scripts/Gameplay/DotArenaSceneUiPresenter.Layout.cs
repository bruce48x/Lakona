#nullable enable

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
        private void BindMatchRankingPanel()
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

            BindMatchRankingPanelContents();
        }

        private void BindMatchRankingPanelContents()
        {
            if (_matchRankingPanel == null)
            {
                return;
            }

            _matchRankingTitleText = BindMatchRankingText(_matchRankingPanel.transform, "TitleText");
            _matchRankingHeaderText = BindMatchRankingText(_matchRankingPanel.transform, "HeaderText");

            _matchRankingRows.Clear();
            for (var i = 0; i < MatchRankingMaxRows; i++)
            {
                var row = BindMatchRankingRow(i);
                if (row != null)
                {
                    _matchRankingRows.Add(row);
                }
            }
        }

        private TMP_Text? BindMatchRankingText(Transform parent, string name)
        {
            var target = parent.Find(name);
            if (target == null || !target.TryGetComponent<TMP_Text>(out var text))
            {
                WarnMissingAuthoredSceneUi($"{BuildSceneUiPath(parent)}/{name}");
                return null;
            }

            return text;
        }

        private MatchRankingRowUi? BindMatchRankingRow(int index)
        {
            var rowName = $"Row{index + 1}";
            var rowTransform = _matchRankingPanel!.transform.Find(rowName);
            if (rowTransform == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/MatchRankingPanel/{rowName}");
                return null;
            }

            var rowObject = rowTransform.gameObject;
            var background = rowObject.GetComponent<Image>();
            if (background == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/MatchRankingPanel/{rowName} Image");
                return null;
            }

            var rankText = BindMatchRankingText(rowObject.transform, "RankText");
            var nameText = BindMatchRankingText(rowObject.transform, "NameText");
            var massText = BindMatchRankingText(rowObject.transform, "MassText");
            if (rankText == null || nameText == null || massText == null)
            {
                return null;
            }

            return new MatchRankingRowUi(rowObject, background, rankText, nameText, massText);
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
