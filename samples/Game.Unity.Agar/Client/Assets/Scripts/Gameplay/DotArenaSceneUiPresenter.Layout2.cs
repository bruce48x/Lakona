#nullable enable

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
        private void EnsureMenuBackground()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _menuBackground = FindSceneUiObject("SceneUI/MenuBackground");
            if (_menuBackground == null)
            {
                WarnMissingAuthoredSceneUi("SceneUI/MenuBackground");
                return;
            }

            _menuBackground.transform.SetAsFirstSibling();

            var rect = (RectTransform)_menuBackground.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (_menuBackground.TryGetComponent<Image>(out var image))
            {
                Object.Destroy(image);
            }

            var gradient = _menuBackground.GetComponent<DotArenaGradientGraphic>()
                ?? _menuBackground.AddComponent<DotArenaGradientGraphic>();
            gradient.TopLeft = new Color(0.88f, 0.98f, 1f, 1f);
            gradient.TopRight = new Color(0.98f, 1f, 0.97f, 1f);
            gradient.BottomLeft = new Color(0.78f, 0.95f, 1f, 1f);
            gradient.BottomRight = new Color(1f, 0.91f, 0.86f, 1f);
            gradient.raycastTarget = false;
        }

        private static void StretchButtonLabel(RectTransform labelRect)
        {
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private void EnsureLobbyQuickActionsText()
        {
            if (_sceneUiRoot == null || _lobbyPanel == null || _lobbyQuickActionsText != null)
            {
                return;
            }

            _lobbyQuickActionsText = FindSceneUiText("SceneUI/LobbyPanel/QuickActionsText");
            if (_lobbyQuickActionsText != null)
            {
                return;
            }

            WarnMissingAuthoredSceneUi("SceneUI/LobbyPanel/QuickActionsText");
        }

        private void WarnIfMissingLobbyQuickActionButtons()
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            WarnIfMissingLobbyQuickActionButton("QuickActionButton1");
            WarnIfMissingLobbyQuickActionButton("QuickActionButton2");
            WarnIfMissingLobbyQuickActionButton("QuickActionButton3");
            WarnIfMissingLobbyQuickActionButton("QuickActionButton4");
        }

        private void WarnIfMissingLobbyQuickActionButton(string name)
        {
            if (FindSceneUiButton($"SceneUI/LobbyPanel/{name}") != null)
            {
                return;
            }

            WarnMissingAuthoredSceneUi($"SceneUI/LobbyPanel/{name}");
        }

        private void EnsureSettlementPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _settlementPanel = FindSceneUiObject("SceneUI/SettlementPanel");
            if (_settlementPanel != null)
            {
                EnsureSettlementPanelContents();
                _settlementPanel.SetActive(false);
                return;
            }

            WarnMissingAuthoredSceneUi("SceneUI/SettlementPanel");
        }

        private void EnsureSettlementPanelContents()
        {
            if (_settlementPanel == null)
            {
                return;
            }

            var panelRect = (RectTransform)_settlementPanel.transform;
            panelRect.sizeDelta = new Vector2(460f, 430f);

            EnsureSettlementText("TitleText", new Vector2(0f, -18f), new Vector2(360f, 32f), 22f, FontStyles.Bold);
            EnsureSettlementText("DetailText", new Vector2(0f, -58f), new Vector2(360f, 122f), 12f, FontStyles.Normal);
            EnsureSettlementText("RewardText", new Vector2(0f, -190f), new Vector2(360f, 34f), 13f, FontStyles.Normal);
            EnsureSettlementText("TaskText", new Vector2(0f, -230f), new Vector2(360f, 1f), 1f, FontStyles.Normal);
            EnsureSettlementText("NextStepText", new Vector2(0f, -250f), new Vector2(360f, 42f), 13f, FontStyles.Normal);

            if (FindSceneUiButton("SceneUI/SettlementPanel/PrimaryButton") == null)
            {
                WarnMissingAuthoredSceneUi("SceneUI/SettlementPanel/PrimaryButton");
            }

            if (FindSceneUiButton("SceneUI/SettlementPanel/SecondaryButton") == null)
            {
                WarnMissingAuthoredSceneUi("SceneUI/SettlementPanel/SecondaryButton");
            }

            LayoutSettlementButton("PrimaryButton", new Vector2(0f, -330f), new Vector2(260f, 32f), "Play Again");
            LayoutSettlementButton("SecondaryButton", new Vector2(0f, -372f), new Vector2(260f, 32f), "Return to Lobby");
        }

        private void EnsureSettlementText(string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles)
        {
            var text = FindSceneUiText($"SceneUI/SettlementPanel/{name}");
            if (text == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/SettlementPanel/{name}");
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
            text.alignment = TextAlignmentOptions.Center;
        }

        private void LayoutSettlementButton(string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            var button = FindSceneUiButton($"SceneUI/SettlementPanel/{name}");
            if (button == null)
            {
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
            }

            var text = FindSceneUiText($"SceneUI/SettlementPanel/{name}/Label");
            if (text != null)
            {
                StretchButtonLabel(text.rectTransform);
                text.text = label;
            }
        }

        private void EnsureMatchmakingPanel()
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            _matchmakingPanel = FindSceneUiObject("SceneUI/MatchmakingPanel");
            if (_matchmakingPanel != null)
            {
                _matchmakingPanel.SetActive(false);
                return;
            }

            WarnMissingAuthoredSceneUi("SceneUI/MatchmakingPanel");
        }
    }
}
