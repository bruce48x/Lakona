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
                _menuBackground = DotArenaUiFactory.CreatePanel(_sceneUiRoot.transform, "MenuBackground");
            }

            _menuBackground.transform.SetAsFirstSibling();

            var rect = (RectTransform)_menuBackground.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = _menuBackground.GetComponent<Image>();
            image.sprite = _uiBackgroundSprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = _uiBackgroundSprite != null ? Color.white : new Color(0.02f, 0.04f, 0.07f, 1f);
            image.raycastTarget = false;
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
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(460f, 330f);

            EnsureEntryTextLayout("TitleText", new Vector2(0f, -44f), new Vector2(360f, 32f), 22f, FontStyles.Bold);
            EnsureEntryTextLayout("StatusText", new Vector2(0f, -76f), new Vector2(360f, 22f), 13f, FontStyles.Normal);
            StretchEntryChildPanel(_modeSelectPanel);
            EnsureEntryContentPanel(_multiplayerPanel, new Vector2(0f, -88f), new Vector2(330f, 212f));
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
            text.alignment = TextAlignmentOptions.Center;
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

            EnsureModeSelectButton("SinglePlayerButton", new Vector2(0f, -132f), "单机：普通模式");
            EnsureModeSelectButton("InvincibleSinglePlayerButton", new Vector2(0f, -190f), "单机：无敌模式");
            EnsureModeSelectButton("MultiplayerButton", new Vector2(0f, -248f), "联机");
        }

        private void EnsureModeSelectButton(string name, Vector2 anchoredPosition, string label)
        {
            var button = FindSceneUiButton($"SceneUI/EntryPanel/ModeSelectPanel/{name}");
            if (button == null)
            {
                CreateSettlementButton(_modeSelectPanel!.transform, name, anchoredPosition, new Vector2(260f, 38f), label);
                button = FindSceneUiButton($"SceneUI/EntryPanel/ModeSelectPanel/{name}");
            }

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
                rect.sizeDelta = new Vector2(300f, 42f);
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
            EnsureMultiplayerTextLayout("AccountLabel", new Vector2(0f, -4f), new Vector2(260f, 18f), 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            EnsureMultiplayerInputLayout("AccountInput", new Vector2(0f, -28f), new Vector2(260f, 30f));
            EnsureMultiplayerTextLayout("PasswordLabel", new Vector2(0f, -64f), new Vector2(260f, 18f), 12f, FontStyles.Normal, TextAlignmentOptions.Left);
            EnsureMultiplayerInputLayout("PasswordInput", new Vector2(0f, -88f), new Vector2(260f, 30f));
            EnsureMultiplayerAuthButton("MatchButton", new Vector2(-70f, -128f), new Vector2(124f, 30f), "登录");
            EnsureMultiplayerAuthButton("BackButton", new Vector2(70f, -128f), new Vector2(124f, 30f), "返回");
            EnsureMultiplayerAuthButton("GuestLoginButton", new Vector2(0f, -166f), new Vector2(260f, 30f), "游客登录");
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
            var input = FindSceneUiInputField($"SceneUI/EntryPanel/MultiplayerPanel/{name}");
            if (input == null)
            {
                return;
            }

            var rect = input.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            if (input.textViewport != null)
            {
                input.textViewport.offsetMin = new Vector2(12f, 5f);
                input.textViewport.offsetMax = new Vector2(-12f, -5f);
            }
        }

        private void EnsureMultiplayerAuthButton(string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            var button = FindSceneUiButton($"SceneUI/EntryPanel/MultiplayerPanel/{name}");
            if (button == null)
            {
                CreateSettlementButton(_multiplayerPanel!.transform, name, anchoredPosition, size, label);
                button = FindSceneUiButton($"SceneUI/EntryPanel/MultiplayerPanel/{name}");
            }

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

            CreateLobbyText(_lobbyPanel.transform, "QuickActionsText", new Vector2(-18f, -150f), new Vector2(380f, 40f), 12f, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            _lobbyQuickActionsText = FindSceneUiText("SceneUI/LobbyPanel/QuickActionsText");
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

            CreateLobbyButton(_lobbyPanel!.transform, name, anchoredPosition, new Vector2(132f, 34f), "Action");
        }

        private void CreateLobbyText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles, TextAlignmentOptions alignment)
        {
            UiFactory.CreateText(
                parent,
                name,
                DotArenaUiRect.TopCenter(anchoredPosition, size),
                DotArenaUiStyleCatalog.LobbyText(fontSize, fontStyles, alignment));
        }

        private void CreateLobbyButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            UiFactory.CreateButton(parent, name, DotArenaUiRect.TopCenter(anchoredPosition, size), label);
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
                return;
            }

            _settlementPanel = DotArenaUiFactory.CreatePanel(_sceneUiRoot.transform, "SettlementPanel");
            var panelRect = (RectTransform)_settlementPanel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(460f, 430f);
            EnsureSettlementPanelContents();
            _settlementPanel.SetActive(false);
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
                CreateSettlementButton(_settlementPanel.transform, "PrimaryButton", new Vector2(0f, -330f), new Vector2(260f, 32f), "再来一局");
            }

            if (FindSceneUiButton("SceneUI/SettlementPanel/SecondaryButton") == null)
            {
                CreateSettlementButton(_settlementPanel.transform, "SecondaryButton", new Vector2(0f, -372f), new Vector2(260f, 32f), "返回大厅");
            }

            LayoutSettlementButton("PrimaryButton", new Vector2(0f, -330f), new Vector2(260f, 32f), "再来一局");
            LayoutSettlementButton("SecondaryButton", new Vector2(0f, -372f), new Vector2(260f, 32f), "返回大厅");
        }

        private void EnsureSettlementText(string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles)
        {
            var text = FindSceneUiText($"SceneUI/SettlementPanel/{name}");
            if (text == null)
            {
                CreateSettlementText(_settlementPanel!.transform, name, anchoredPosition, size, fontSize, fontStyles);
                text = FindSceneUiText($"SceneUI/SettlementPanel/{name}");
            }

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
                return;
            }

            _matchmakingPanel = DotArenaUiFactory.CreatePanel(_sceneUiRoot.transform, "MatchmakingPanel");
            var panelRect = (RectTransform)_matchmakingPanel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(420f, 240f);

            CreateSettlementText(_matchmakingPanel.transform, "TitleText", new Vector2(0f, -18f), new Vector2(340f, 32f), 22f, FontStyles.Bold);
            CreateSettlementText(_matchmakingPanel.transform, "DetailText", new Vector2(0f, -62f), new Vector2(340f, 100f), 13f, FontStyles.Normal);
            CreateSettlementButton(_matchmakingPanel.transform, "CancelButton", new Vector2(0f, -188f), new Vector2(260f, 32f), "取消匹配");
            _matchmakingPanel.SetActive(false);
        }

        private void CreateSettlementText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize, FontStyles fontStyles)
        {
            UiFactory.CreateText(
                parent,
                name,
                DotArenaUiRect.TopCenter(anchoredPosition, size),
                DotArenaUiStyleCatalog.CenterPanelText(fontSize, fontStyles));
        }

        private void CreateSettlementButton(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, string label)
        {
            UiFactory.CreateButton(parent, name, DotArenaUiRect.TopCenter(anchoredPosition, size), label);
        }
    }
}
