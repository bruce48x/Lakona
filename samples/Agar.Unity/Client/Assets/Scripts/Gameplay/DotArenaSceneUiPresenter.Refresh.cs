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
            const bool showDebug = false;
            var showLobby = !showSettlement &&
                            !showMatchmaking &&
                            !snapshot.HasSession &&
                            snapshot.EntryMenuState == EntryMenuState.MultiplayerLobby;
            var showEntry = !showSettlement && !showMatchmaking && !showHud && !showLobby;
            var showMenuBackground = showEntry || showLobby || showMatchmaking || showSettlement;

            if (_menuBackground != null) _menuBackground.SetActive(showMenuBackground);
            if (_hudPanel != null) _hudPanel.SetActive(showHud);
            if (_matchRankingPanel != null) _matchRankingPanel.SetActive(showHud);
            if (_debugPanel != null) _debugPanel.SetActive(showDebug);
            if (_entryPanel != null) _entryPanel.SetActive(showEntry);
            if (_matchmakingPanel != null) _matchmakingPanel.SetActive(showMatchmaking);
            if (_settlementPanel != null) _settlementPanel.SetActive(showSettlement);
            if (_lobbyPanel != null) _lobbyPanel.SetActive(showLobby);
            if (_modeSelectPanel != null) _modeSelectPanel.SetActive(showEntry && snapshot.EntryMenuState == EntryMenuState.ModeSelect);
            if (_multiplayerPanel != null) _multiplayerPanel.SetActive(showEntry && snapshot.EntryMenuState == EntryMenuState.MultiplayerAuth);

            SetText(_hudStatusText, "状态：对局中");
            SetText(_hudPlayerText, $"玩家：{(snapshot.LocalPlayerId.Length > 0 ? snapshot.LocalPlayerId : snapshot.Account)}   质量：{snapshot.LocalPlayerMassText}   胜场：{snapshot.LocalWinCount}");
            SetText(_hudTickText, string.Empty);
            SetText(_hudTitleText, string.Empty);
            SetText(_hudModeText, string.Empty);
            SetText(_hudHintText, string.Empty);
            SetText(_hudEventText, string.Empty);
            SetText(_matchRankingTitleText, "实时排名");
            SetText(_matchRankingHeaderText, "名次    玩家              质量");
            RefreshMatchRankingRows(snapshot.MatchRankingEntries, showHud);
            SetText(_debugTitleText, string.Empty);
            SetText(_debugDetailText, string.Empty);
            if (snapshot.HasSession && snapshot.SessionMode == SessionMode.Multiplayer)
            {
                if (snapshot.LastRoundRemainingSeconds > 0)
                {
                    var minutes = snapshot.LastRoundRemainingSeconds / 60;
                    var seconds = snapshot.LastRoundRemainingSeconds % 60;
                    SetText(_hudCountdownText, $"剩余 {minutes:D2}:{seconds:D2}");
                }
                else
                {
                    SetText(_hudCountdownText, "剩余 --:--");
                }
            }
            else
            {
                SetText(_hudCountdownText, string.Empty);
            }

            SetText(_entryTitleText, "点阵竞技场");
            SetText(_entryStatusText, snapshot.EntryMenuState == EntryMenuState.MultiplayerAuth ? string.Empty : snapshot.Status);
            SetText(_matchmakingTitleText, snapshot.SessionMode == SessionMode.SinglePlayer ? "准备本地对局" : snapshot.MatchmakingTitle);
            SetText(_matchmakingDetailText, snapshot.MatchmakingDetail);
            SetText(_matchmakingCancelButtonText, "取消匹配");
            SetText(_lobbyTitleText, _lobbyUi.GetLobbyTabTitle(snapshot));
            SetText(_lobbySummaryText, snapshot.MetaPlayerSummary);
            SetText(_lobbyHighlightsText, _lobbyUi.GetLobbyHighlightsText(snapshot));
            SetText(_lobbyQuickActionsText, _lobbyUi.GetLobbyQuickActionsText(snapshot));
            _lobbyUi.RefreshLobbyQuickActionButtons(snapshot, _lobbyQuickActionButton1, _lobbyQuickActionButton1Text, _lobbyQuickActionButton2, _lobbyQuickActionButton2Text, _lobbyQuickActionButton3, _lobbyQuickActionButton3Text, _lobbyQuickActionButton4, _lobbyQuickActionButton4Text);
            SetText(_lobbyDetailText, _lobbyUi.GetLobbyTabDetail(snapshot));
            SetText(_lobbyFooterText, snapshot.MetaFooterHint);
            SetText(_lobbyPrimaryActionButtonText, _lobbyUi.GetLobbyPrimaryActionLabel(snapshot));
            SetText(_lobbySecondaryActionButtonText, _lobbyUi.GetLobbySecondaryActionLabel(snapshot));
            _lobbyUi.ApplyLobbyActionLayout(snapshot, _lobbyPanel?.GetComponent<RectTransform>(), _lobbyDetailText?.rectTransform, _lobbyPrimaryActionButton?.GetComponent<RectTransform>(), _lobbySecondaryActionButton?.GetComponent<RectTransform>(), _lobbyFooterText?.rectTransform);
            SetText(_multiplayerSubtitleText, string.Empty);
            SetText(_accountLabelText, "账号");
            SetText(_passwordLabelText, "密码");
            SetText(_accountPlaceholderText, "请输入账号");
            SetText(_passwordPlaceholderText, "请输入密码");
            SetText(_singlePlayerButtonText, "单机：普通模式");
            SetText(_invincibleSinglePlayerButtonText, "单机：无敌模式");
            SetText(_multiplayerButtonText, "联机");
            SetText(_matchButtonText, snapshot.IsConnecting ? "登录中..." : "登录");
            SetText(_guestLoginButtonText, snapshot.IsConnecting ? "申请中..." : "游客登录");
            SetText(_backButtonText, "返回");

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
            if (_lobbySettingsButton != null) _lobbySettingsButton.interactable = !snapshot.IsBusy && !_lobbyUi.IsSelected(MetaTab.Settings);
            if (_lobbyPrimaryActionButton != null) _lobbyPrimaryActionButton.gameObject.SetActive(_lobbyUi.HasLobbyPrimaryAction());
            if (_lobbySecondaryActionButton != null) _lobbySecondaryActionButton.gameObject.SetActive(_lobbyUi.HasLobbySecondaryAction());
            if (_lobbyPrimaryActionButton != null) _lobbyPrimaryActionButton.interactable = !snapshot.IsBusy;
            if (_lobbySecondaryActionButton != null) _lobbySecondaryActionButton.interactable = !snapshot.IsBusy;
            if (_lobbyQuickActionsText != null) _lobbyQuickActionsText.gameObject.SetActive(!string.IsNullOrWhiteSpace(_lobbyQuickActionsText.text));
            if (_lobbyQuickActionButton1 != null) _lobbyQuickActionButton1.interactable = !snapshot.IsBusy;
            if (_lobbyQuickActionButton2 != null) _lobbyQuickActionButton2.interactable = !snapshot.IsBusy;
            if (_lobbyQuickActionButton3 != null) _lobbyQuickActionButton3.interactable = !snapshot.IsBusy;
            if (_lobbyQuickActionButton4 != null) _lobbyQuickActionButton4.interactable = !snapshot.IsBusy;
            if (_accountInputField != null) _accountInputField.interactable = !snapshot.IsBusy;
            if (_passwordInputField != null) _passwordInputField.interactable = !snapshot.IsBusy;

            SyncSceneUiInputs(snapshot.Account, snapshot.Password);
            SetText(_settlementTitleText, snapshot.SessionMode == SessionMode.Multiplayer ? "联机结算" : "单机结算");
            SetText(_settlementDetailText, snapshot.SettlementDetail);
            SetText(_settlementRewardText, snapshot.SettlementRewardSummary);
            SetText(_settlementTaskText, string.Empty);
            if (_settlementTaskText != null) _settlementTaskText.gameObject.SetActive(false);
            SetText(_settlementNextStepText, snapshot.SettlementNextStepSummary);
            SetText(_settlementPrimaryButtonText, snapshot.SettlementPrimaryActionText);
            SetText(_settlementSecondaryButtonText, "返回大厅");
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
                SetText(row.NameText, entry.PlayerId);
                SetText(row.MassText, DotArenaPresentation.FormatMass(entry.Mass));

                var rowBackground = (i & 1) == 0
                    ? new Color(0.08f, 0.12f, 0.16f, 0.10f)
                    : new Color(0.04f, 0.07f, 0.1f, 0.06f);
                row.Background.color = entry.IsLocalPlayer
                    ? new Color(0.16f, 0.36f, 0.38f, 0.20f)
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
        }
    }
}
