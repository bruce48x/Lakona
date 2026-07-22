#nullable enable

using System.Collections.Generic;
using UnityEngine;
using static SampleClient.Gameplay.DotArenaTuning;

namespace SampleClient.Gameplay
{
    internal sealed partial class DotArenaSceneUiPresenter
    {
        public void Refresh(in DotArenaSceneUiSnapshot snapshot)
        {
            if (_sceneUiRoot == null)
            {
                return;
            }

            var showSettlement = snapshot.FlowState == FrontendFlowState.Settlement;
            var showMatchmaking = snapshot.FlowState == FrontendFlowState.Matchmaking;
            var showHud = snapshot.HasSession && snapshot.FlowState == FrontendFlowState.InMatch;
            var showCountdown = showHud && snapshot.SessionMode == SessionMode.Multiplayer;
            var showLobby = !showSettlement &&
                            !showMatchmaking &&
                            !snapshot.HasSession &&
                            snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby;
            var showEntry = !showSettlement && !showMatchmaking && !showHud && !showLobby;
            var showMenuBackground = showEntry || showLobby || showMatchmaking || showSettlement;

            if (_menuBackground != null) _menuBackground.SetActive(showMenuBackground);
            if (_hudPanel != null) _hudPanel.SetActive(showHud);
            if (_matchRankingPanel != null) _matchRankingPanel.SetActive(showHud);
            if (_matchmakingPanel != null) _matchmakingPanel.SetActive(showMatchmaking);
            if (_settlementPanel != null) _settlementPanel.SetActive(showSettlement);
            if (_lobbyPanel != null) _lobbyPanel.SetActive(showLobby);
            if (_modeSelectPanel != null) _modeSelectPanel.SetActive(showEntry && snapshot.EntryMenuState == EntryMenuState.ModeSelect);
            if (_loginPanel != null) _loginPanel.SetActive(showEntry && snapshot.EntryMenuState == EntryMenuState.MultiplayerAuth);

            SetText(_hudTitleText, string.Empty);
            SetText(_hudModeText, string.Empty);
            SetText(_hudHintText, string.Empty);
            SetText(_matchRankingTitleText, "Live Ranking");
            SetText(_matchRankingHeaderText, "Rank   Player       Mass");
            RefreshMatchRankingRows(snapshot.MatchRankingEntries, showHud);
            if (_hudCountdownText != null) _hudCountdownText.gameObject.SetActive(showCountdown);
            if (showCountdown)
            {
                if (snapshot.LastRoundRemainingSeconds > 0)
                {
                    var minutes = snapshot.LastRoundRemainingSeconds / 60;
                    var seconds = snapshot.LastRoundRemainingSeconds % 60;
                    SetText(_hudCountdownText, $"Remaining {minutes:D2}:{seconds:D2}");
                }
                else
                {
                    SetText(_hudCountdownText, "Remaining --:--");
                }
            }
            else
            {
                SetText(_hudCountdownText, string.Empty);
            }

            SetText(_entryTitleText, "Dot Arena");
            SetText(_entryStatusText, snapshot.EntryMenuState == EntryMenuState.MultiplayerAuth ? string.Empty : snapshot.Status);
            SetText(_matchmakingTitleText, snapshot.SessionMode == SessionMode.SinglePlayer ? "Preparing local match" : snapshot.MatchmakingTitle);
            SetText(_matchmakingDetailText, snapshot.MatchmakingDetail);
            SetText(_matchmakingCancelButtonText, "Cancel Matchmaking");
            SetText(_lobbyTitleText, _lobbyUi.GetLobbyTabTitle(snapshot));
            RefreshLobbyContents(snapshot, showLobby);
            SetText(_lobbyPrimaryActionButtonText, _lobbyUi.GetLobbyPrimaryActionLabel(snapshot));
            SetText(_lobbySecondaryActionButtonText, _lobbyUi.GetLobbySecondaryActionLabel(snapshot));
            SetText(_multiplayerSubtitleText, string.Empty);
            SetText(_accountLabelText, "Account");
            SetText(_passwordLabelText, "Password");
            SetText(_accountPlaceholderText, "Enter account");
            SetText(_passwordPlaceholderText, "Enter password");
            SetText(_singlePlayerButtonText, "Single Player: Normal");
            SetText(_invincibleSinglePlayerButtonText, "Single Player: Invincible");
            SetText(_multiplayerButtonText, "Multiplayer");
            SetText(_matchButtonText, snapshot.IsConnecting ? "Logging in..." : "Login");
            SetText(_guestLoginButtonText, snapshot.IsConnecting ? "Requesting..." : "Guest Login");
            SetText(_backButtonText, "Back");

            if (_singlePlayerButton != null) _singlePlayerButton.interactable = !snapshot.IsBusy;
            if (_invincibleSinglePlayerButton != null) _invincibleSinglePlayerButton.interactable = !snapshot.IsBusy;
            if (_multiplayerButton != null) _multiplayerButton.interactable = !snapshot.IsBusy;
            if (_matchButton != null) _matchButton.interactable = !snapshot.IsBusy;
            if (_guestLoginButton != null) _guestLoginButton.interactable = !snapshot.IsBusy;
            if (_backButton != null) _backButton.interactable = !snapshot.IsBusy;
            if (_matchmakingCancelButton != null) _matchmakingCancelButton.interactable = !snapshot.IsBusy;
            if (_lobbyProfileButton != null) _lobbyProfileButton.interactable = !snapshot.IsBusy && !_lobbyUi.IsSelected(MetaTab.Lobby);
            if (_lobbyTasksButton != null) _lobbyTasksButton.gameObject.SetActive(false);
            if (_lobbyShopButton != null) _lobbyShopButton.gameObject.SetActive(false);
            if (_lobbyRecordsButton != null) _lobbyRecordsButton.gameObject.SetActive(false);
            if (_lobbyLeaderboardButton != null) _lobbyLeaderboardButton.interactable = !snapshot.IsBusy && !_lobbyUi.IsSelected(MetaTab.Leaderboard);
            if (_lobbyPrimaryActionButton != null) _lobbyPrimaryActionButton.gameObject.SetActive(_lobbyUi.HasLobbyPrimaryAction());
            if (_lobbySecondaryActionButton != null) _lobbySecondaryActionButton.gameObject.SetActive(_lobbyUi.HasLobbySecondaryAction());
            if (_lobbyPrimaryActionButton != null) _lobbyPrimaryActionButton.interactable = !snapshot.IsBusy;
            if (_lobbySecondaryActionButton != null) _lobbySecondaryActionButton.interactable = !snapshot.IsBusy;
            if (_accountInputField != null) _accountInputField.interactable = !snapshot.IsBusy;
            if (_passwordInputField != null) _passwordInputField.interactable = !snapshot.IsBusy;
            if (_accountLegacyInputField != null) _accountLegacyInputField.interactable = !snapshot.IsBusy;
            if (_passwordLegacyInputField != null) _passwordLegacyInputField.interactable = !snapshot.IsBusy;

            SyncSceneUiInputs(snapshot.Account, snapshot.Password);
            SetText(_settlementTitleText, snapshot.SessionMode == SessionMode.Multiplayer ? "Multiplayer results" : "Single-player results");
            SetText(_settlementDetailText, snapshot.SettlementDetail);
            SetText(_settlementRewardText, snapshot.SettlementRewardSummary);
            SetText(_settlementTaskText, string.Empty);
            if (_settlementTaskText != null) _settlementTaskText.gameObject.SetActive(false);
            SetText(_settlementNextStepText, snapshot.SettlementNextStepSummary);
            SetText(_settlementPrimaryButtonText, snapshot.SettlementPrimaryActionText);
            SetText(_settlementSecondaryButtonText, "Return to Lobby");
        }

        private void RefreshLobbyContents(in DotArenaSceneUiSnapshot snapshot, bool showLobby)
        {
            var showProfile = showLobby && _lobbyUi.IsSelected(MetaTab.Lobby);
            var showLeaderboard = showLobby && _lobbyUi.IsSelected(MetaTab.Leaderboard);
            if (_lobbyProfileContent != null) _lobbyProfileContent.SetActive(showProfile);
            if (_lobbyLeaderboardContent != null) _lobbyLeaderboardContent.SetActive(showLeaderboard);

            SetText(_lobbyProfilePlayerText, $"Player: {NormalizePlayerId(snapshot.ProfilePlayerId)}");
            SetText(_lobbyProfileWinsText, $"Wins\n{snapshot.ProfileWinCount}");
            SetText(_lobbyProfileVictoryPointsText, $"Victory Points\n{snapshot.ProfileVictoryPoints}");

            var periodLabel = string.IsNullOrWhiteSpace(snapshot.LeaderboardPeriodStartUtc)
                ? "Weekly leaderboard"
                : $"Week of {snapshot.LeaderboardPeriodStartUtc}";
            var resetLabel = snapshot.LeaderboardSecondsUntilReset > 0
                ? $"Resets in {FormatResetTime(snapshot.LeaderboardSecondsUntilReset)}"
                : "Reset pending";
            SetText(_lobbyLeaderboardPeriodText, $"{periodLabel} | {resetLabel}");
            SetText(_lobbyLeaderboardHeaderText, "Rank<pos=12%>Player<pos=70%>VP<pos=88%>Wins");

            var entries = snapshot.LeaderboardEntries;
            var hasEntries = entries != null && entries.Count > 0;
            if (_lobbyLeaderboardEmptyText != null) _lobbyLeaderboardEmptyText.gameObject.SetActive(showLeaderboard && !hasEntries);
            for (var index = 0; index < _lobbyLeaderboardRows.Count; index++)
            {
                var row = _lobbyLeaderboardRows[index];
                var showRow = showLeaderboard && entries != null && index < entries.Count;
                row.gameObject.SetActive(showRow);
                if (!showRow || entries == null)
                {
                    continue;
                }

                var entry = entries[index];
                var player = NormalizePlayerId(entry.PlayerId) + (entry.IsLocalPlayer ? " (You)" : string.Empty);
                row.text = $"#{entry.Rank}<pos=12%>{player}<pos=70%>{entry.VictoryPoints}<pos=88%>{entry.WinCount}";
            }
        }

        private static string NormalizePlayerId(string playerId)
        {
            return string.IsNullOrWhiteSpace(playerId)
                ? "—"
                : playerId.Replace("<", "‹").Replace(">", "›");
        }

        private static string FormatResetTime(int seconds)
        {
            seconds = Mathf.Max(0, seconds);
            var days = seconds / 86400;
            var hours = (seconds % 86400) / 3600;
            var minutes = (seconds % 3600) / 60;
            return days > 0 ? $"{days}d {hours}h" : hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        }

        private void RefreshMatchRankingRows(IReadOnlyList<DotArenaMatchRankingEntry>? entries, bool showHud)
        {
            for (var i = 0; i < _matchRankingRows.Count; i++)
            {
                var row = _matchRankingRows[i];
                var showRow = showHud && entries != null && i < entries.Count;
                row.Root.SetActive(showRow);
                if (!showRow || entries == null)
                {
                    continue;
                }

                var entry = entries[i];
                SetText(row.RankText, $"#{entry.Rank}");
                SetText(row.NameText, entry.DisplayName);
                SetText(row.MassText, DotArenaPresentation.FormatMass(entry.Mass));

                var rowBackground = (i & 1) == 0
                    ? new Color(0.76f, 0.94f, 0.96f, 0.26f)
                    : new Color(0.98f, 1f, 1f, 0.16f);
                row.Background.color = entry.IsLocalPlayer
                    ? new Color(1f, 0.63f, 0.42f, 0.34f)
                    : rowBackground;

                var nameColor = entry.IsLocalPlayer ? UiAccentTextColor : UiPrimaryTextColor;
                var valueColor = entry.IsLocalPlayer ? UiAccentTextColor : UiSecondaryTextColor;
                row.RankText.color = valueColor;
                row.NameText.color = nameColor;
                row.MassText.color = valueColor;
            }
        }

        private void SyncSceneUiInputs(string account, string password)
        {
            if (_accountInputField != null && !_accountInputField.isFocused && _accountInputField.text != account)
            {
                _accountInputField.SetTextWithoutNotify(account);
            }

            if (_passwordInputField != null && !_passwordInputField.isFocused && _passwordInputField.text != password)
            {
                _passwordInputField.SetTextWithoutNotify(password);
            }

            if (_accountLegacyInputField != null && !_accountLegacyInputField.isFocused && _accountLegacyInputField.text != account)
            {
                _accountLegacyInputField.SetTextWithoutNotify(account);
            }

            if (_passwordLegacyInputField != null && !_passwordLegacyInputField.isFocused && _passwordLegacyInputField.text != password)
            {
                _passwordLegacyInputField.SetTextWithoutNotify(password);
            }
        }
    }
}
