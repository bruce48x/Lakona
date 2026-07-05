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

        private void EnsureEntryPanelLayout()
        {
            if (_entryPanel == null)
            {
                return;
            }

            var panelRect = (RectTransform)_entryPanel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, 0f);
            panelRect.sizeDelta = new Vector2(1050f, 430f);

            EnsureEntryTextLayout("TitleText", new Vector2(-315f, -56f), new Vector2(360f, 48f), 34f, FontStyles.Bold);
            EnsureEntryTextLayout("StatusText", new Vector2(-315f, -114f), new Vector2(360f, 76f), 14f, FontStyles.Normal);
            StretchEntryChildPanel(_modeSelectPanel);
            EnsureEntryContentPanel(_multiplayerPanel, new Vector2(260f, -80f), new Vector2(360f, 242f));
        }

        private void EnsureEntryTextLayout(string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles)
        {
            var text = FindSceneUiText($"SceneUI/EntryPanel/{name}");
            if (text == null)
            {
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
            text.alignment = TextAlignmentOptions.Left;
        }

        private static void StretchEntryChildPanel(GameObject? panel)
        {
            if (panel == null)
            {
                return;
            }

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureEntryContentPanel(GameObject? panel, Vector2 anchoredPosition, Vector2 size)
        {
            if (panel == null)
            {
                return;
            }

            var rect = (RectTransform)panel.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private void EnsureModeSelectPanelContents()
        {
            if (_modeSelectPanel == null)
            {
                return;
            }

            EnsureModeSelectButton("SinglePlayerButton", new Vector2(250f, -82f), "Single Player: Normal");
            EnsureModeSelectButton("InvincibleSinglePlayerButton", new Vector2(250f, -154f), "Single Player: Invincible");
            EnsureModeSelectButton("MultiplayerButton", new Vector2(250f, -226f), "Multiplayer");
        }

        private void EnsureModeSelectButton(string name, Vector2 anchoredPosition, string label)
        {
            var button = FindSceneUiButton($"SceneUI/EntryPanel/ModeSelectPanel/{name}");
            if (button == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/EntryPanel/ModeSelectPanel/{name}");
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = new Vector2(360f, 54f);
            }

            var text = FindSceneUiText($"SceneUI/EntryPanel/ModeSelectPanel/{name}/Label");
            if (text != null)
            {
                StretchButtonLabel(text.rectTransform);
                text.text = label;
            }
        }

        private static void StretchButtonLabel(RectTransform labelRect)
        {
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
        }

        private void EnsureMultiplayerAuthActionButtons()
        {
            if (_multiplayerPanel == null)
            {
                return;
            }

            EnsureMultiplayerAuthFormLayout();
        }

        private void EnsureMultiplayerAuthFormLayout()
        {
            EnsureMultiplayerTextLayout("SubtitleText", new Vector2(0f, -2f), new Vector2(300f, 1f), 1f, FontStyles.Bold, TextAlignmentOptions.Center);
            EnsureMultiplayerTextLayout("AccountLabel", new Vector2(0f, -4f), new Vector2(300f, 18f), 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            EnsureMultiplayerInputLayout("AccountInput", new Vector2(0f, -32f), new Vector2(300f, 38f));
            EnsureMultiplayerTextLayout("PasswordLabel", new Vector2(0f, -80f), new Vector2(300f, 18f), 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            EnsureMultiplayerInputLayout("PasswordInput", new Vector2(0f, -108f), new Vector2(300f, 38f));
            EnsureMultiplayerAuthButton("MatchButton", new Vector2(-80f, -166f), new Vector2(140f, 38f), "Login");
            EnsureMultiplayerAuthButton("BackButton", new Vector2(80f, -166f), new Vector2(140f, 38f), "Back");
            EnsureMultiplayerAuthButton("GuestLoginButton", new Vector2(0f, -216f), new Vector2(300f, 38f), "Guest Login");
        }

        private void EnsureMultiplayerTextLayout(string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles, TextAlignmentOptions alignment)
        {
            var text = FindSceneUiText($"SceneUI/EntryPanel/MultiplayerPanel/{name}");
            if (text == null)
            {
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

        private void EnsureMultiplayerInputLayout(string name, Vector2 anchoredPosition, Vector2 size)
        {
            var path = $"SceneUI/EntryPanel/MultiplayerPanel/{name}";
            var rect = FindSceneUiRect(path);
            if (rect == null)
            {
                WarnMissingAuthoredSceneUi(path);
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            var input = FindSceneUiInputField(path);
            if (input?.textViewport != null)
            {
                input.textViewport.offsetMin = new Vector2(12f, 5f);
                input.textViewport.offsetMax = new Vector2(-12f, -5f);
            }

            var legacyInput = FindSceneUiLegacyInputField(path);
            if (legacyInput?.textComponent != null)
            {
                EnsureLegacyInputFieldViewport(legacyInput);
            }
        }

        private void EnsureMultiplayerAuthButton(string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            var button = FindSceneUiButton($"SceneUI/EntryPanel/MultiplayerPanel/{name}");
            if (button == null)
            {
                WarnMissingAuthoredSceneUi($"SceneUI/EntryPanel/MultiplayerPanel/{name}");
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

            var text = FindSceneUiText($"SceneUI/EntryPanel/MultiplayerPanel/{name}/Label");
            if (text != null)
            {
                StretchButtonLabel(text.rectTransform);
                text.text = label;
            }
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

        private void EnsureLobbyQuickActionButtons()
        {
            if (_lobbyPanel == null)
            {
                return;
            }

            EnsureLobbyQuickActionButton("QuickActionButton1", new Vector2(-100f, -194f));
            EnsureLobbyQuickActionButton("QuickActionButton2", new Vector2(100f, -194f));
            EnsureLobbyQuickActionButton("QuickActionButton3", new Vector2(-100f, -236f));
            EnsureLobbyQuickActionButton("QuickActionButton4", new Vector2(100f, -236f));
        }

        private void EnsureLobbyQuickActionButton(string name, Vector2 anchoredPosition)
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
