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
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "Multiplayer lobby",
                MetaTab.Lobby => "Profile",
                MetaTab.Leaderboard => "Leaderboard",
                MetaTab.Settings => "Settings",
                _ => "Lobby"
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
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "Start Matchmaking",
                MetaTab.Lobby => "Change Preset",
                _ => string.Empty
            };
        }

        public string GetLobbySecondaryActionLabel(in DotArenaSceneUiSnapshot snapshot)
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab switch
            {
                MetaTab.Lobby when snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby && snapshot.SessionMode == SessionMode.Multiplayer => "Sign Out",
                MetaTab.Lobby => "View Preset",
                MetaTab.Settings => "Toggle Fullscreen",
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
