#nullable enable

using System;
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

        public void BindLobbyTabButton(Button? button, MetaTab tab, Action<MetaTab> onSelected)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                SelectedTab = NormalizeTab(tab);
                onSelected(SelectedTab);
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
                MetaTab.Lobby => "Profile",
                MetaTab.Leaderboard => "Leaderboard",
                _ => "Lobby"
            };
        }

        public bool HasLobbyPrimaryAction()
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab == MetaTab.Lobby;
        }

        public bool HasLobbySecondaryAction()
        {
            SelectedTab = NormalizeTab(SelectedTab);
            return SelectedTab == MetaTab.Lobby;
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
                _ => string.Empty
            };
        }

        private static MetaTab NormalizeTab(MetaTab tab)
        {
            return tab == MetaTab.Leaderboard ? tab : MetaTab.Lobby;
        }

    }
}
