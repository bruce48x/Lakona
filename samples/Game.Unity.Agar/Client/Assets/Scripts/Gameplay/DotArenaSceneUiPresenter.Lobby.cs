#nullable enable

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SampleClient.Gameplay
{
    internal sealed class DotArenaSceneLobbyUiCoordinator
    {
        public MetaTab SelectedTab { get; private set; } = MetaTab.Lobby;

        public bool IsSelected(MetaTab tab)
        {
            return SelectedTab == tab;
        }

        public void BindLobbyTabButton(Button? button, MetaTab tab)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectedTab = NormalizeTab(tab));
        }

        public void BindLobbyQuickActionButton(Button? button, int index)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                var targetTab = GetLobbyQuickActionTarget(SelectedTab, index);
                if (targetTab.HasValue)
                {
                    SelectedTab = targetTab.Value;
                }
            });
        }

        public void BindLobbyActionButtons(Button? primaryButton, Button? secondaryButton, Action<MetaTab, bool> onLobbyActionRequested)
        {
            if (primaryButton != null)
            {
                primaryButton.onClick.RemoveAllListeners();
                primaryButton.onClick.AddListener(() => onLobbyActionRequested(SelectedTab, true));
            }

            if (secondaryButton != null)
            {
                secondaryButton.onClick.RemoveAllListeners();
                secondaryButton.onClick.AddListener(() => onLobbyActionRequested(SelectedTab, false));
            }
        }

        public string GetLobbyTabTitle(in DotArenaSceneUiSnapshot snapshot)
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "联机大厅",
                MetaTab.Lobby => "资料",
                MetaTab.Leaderboard => "排行榜",
                MetaTab.Settings => "设置",
                _ => "大厅"
            };
        }

        public string GetLobbyTabDetail(in DotArenaSceneUiSnapshot snapshot)
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab switch
            {
                MetaTab.Lobby => snapshot.MetaProfileDetail,
                MetaTab.Leaderboard => snapshot.MetaLeaderboardDetail,
                MetaTab.Settings => snapshot.MetaSettingsDetail,
                _ => snapshot.MetaProfileDetail
            };
        }

        public string GetLobbyHighlightsText(in DotArenaSceneUiSnapshot snapshot)
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab == MetaTab.Lobby ? snapshot.MetaLobbyHighlights : string.Empty;
        }

        public string GetLobbyQuickActionsText(in DotArenaSceneUiSnapshot snapshot)
        {
            return string.Empty;
        }

        public bool HasLobbyPrimaryAction()
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab == MetaTab.Lobby;
        }

        public bool HasLobbySecondaryAction()
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab is MetaTab.Lobby or MetaTab.Settings;
        }

        public string GetLobbyPrimaryActionLabel(in DotArenaSceneUiSnapshot snapshot)
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "开始匹配",
                MetaTab.Lobby => "切换预设",
                _ => string.Empty
            };
        }

        public string GetLobbySecondaryActionLabel(in DotArenaSceneUiSnapshot snapshot)
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "退出登录",
                MetaTab.Lobby => "查看预设",
                MetaTab.Settings => "切换全屏",
                _ => string.Empty
            };
        }

        public string GetLobbyQuickActionHint(in DotArenaSceneUiSnapshot snapshot, int index)
        {
            return string.Empty;
        }

        public void RefreshLobbyQuickActionButtons(
            in DotArenaSceneUiSnapshot snapshot,
            Button? button1,
            TMP_Text? label1,
            Button? button2,
            TMP_Text? label2,
            Button? button3,
            TMP_Text? label3,
            Button? button4,
            TMP_Text? label4)
        {
            HideLobbyQuickActionButton(button1);
            HideLobbyQuickActionButton(button2);
            HideLobbyQuickActionButton(button3);
            HideLobbyQuickActionButton(button4);
        }

        public void ApplyLobbyActionLayout(
            in DotArenaSceneUiSnapshot snapshot,
            RectTransform? panelRect,
            RectTransform? detailRect,
            RectTransform? primaryRect,
            RectTransform? secondaryRect,
            RectTransform? footerRect)
        {
            if (panelRect == null || primaryRect == null || secondaryRect == null || detailRect == null || footerRect == null)
            {
                return;
            }

            var isMultiplayerLobby = snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby &&
                                     snapshot.SessionMode == SessionMode.Multiplayer;

            if (!isMultiplayerLobby)
            {
                SetAnchoredPosition(detailRect, new Vector2(0f, -326f));
                SetSizeDelta(detailRect, new Vector2(980f, 290f));
                SetAnchoredPosition(primaryRect, new Vector2(-120f, -650f));
                SetAnchoredPosition(secondaryRect, new Vector2(120f, -650f));
                SetAnchoredPosition(footerRect, new Vector2(0f, -708f));
                SetSizeDelta(footerRect, new Vector2(980f, 24f));
                return;
            }

            var panelHeight = panelRect.rect.height;
            var footerHeight = footerRect.sizeDelta.y > 0f ? footerRect.sizeDelta.y : 24f;
            var buttonHeight = Mathf.Max(primaryRect.sizeDelta.y, secondaryRect.sizeDelta.y, 42f);
            const float detailTop = 238f;
            const float detailGap = 18f;
            const float buttonGap = 24f;
            const float bottomPadding = 30f;
            const float buttonXOffset = 120f;

            var footerTop = panelHeight - footerHeight - bottomPadding;
            var buttonTop = footerTop - buttonGap - buttonHeight;
            var detailHeight = Mathf.Max(84f, buttonTop - detailTop - detailGap);

            SetAnchoredPosition(detailRect, new Vector2(0f, -detailTop));
            SetSizeDelta(detailRect, new Vector2(980f, detailHeight));
            SetAnchoredPosition(primaryRect, new Vector2(-buttonXOffset, -buttonTop));
            SetAnchoredPosition(secondaryRect, new Vector2(buttonXOffset, -buttonTop));
            SetAnchoredPosition(footerRect, new Vector2(0f, -footerTop));
            SetSizeDelta(footerRect, new Vector2(980f, footerHeight));
        }

        private static MetaTab? GetLobbyQuickActionTarget(MetaTab currentTab, int index)
        {
            return null;
        }

        private static MetaTab NormalizeTab(MetaTab tab)
        {
            return tab is MetaTab.Leaderboard or MetaTab.Settings ? tab : MetaTab.Lobby;
        }

        private void RefreshLobbyQuickActionButton(Button? button, TMP_Text? label, in DotArenaSceneUiSnapshot snapshot, int index)
        {
            if (button == null)
            {
                return;
            }

            var text = GetLobbyQuickActionHint(snapshot, index);
            var hasAction = !string.IsNullOrWhiteSpace(text);
            button.gameObject.SetActive(hasAction);
            if (hasAction)
            {
                SetText(label, text);
            }
        }

        private static void HideLobbyQuickActionButton(Button? button)
        {
            if (button != null)
            {
                button.gameObject.SetActive(false);
            }
        }

        private static void SetAnchoredPosition(RectTransform? rectTransform, Vector2 anchoredPosition)
        {
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = anchoredPosition;
            }
        }

        private static void SetSizeDelta(RectTransform? rectTransform, Vector2 sizeDelta)
        {
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = sizeDelta;
            }
        }

        private static void SetText(TMP_Text? label, string value)
        {
            if (label == null || label.text == value)
            {
                return;
            }

            label.text = value;
        }
    }
}
