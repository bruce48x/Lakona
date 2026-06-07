#nullable enable

using System;
using System.Collections.Generic;

namespace SampleClient.Gameplay
{
    public sealed partial class DotArenaGame
    {
        private sealed partial class DotArenaGameUiSurface
        {
            public DotArenaSceneUiSnapshot BuildSceneUiSnapshot()
            {
                var settlementSummary = _owner._settlementSummary;
                var previewPreset = DotArenaSinglePlayerCatalog.PeekPreset(_owner._singlePlayerPlaylistIndex);
                var endpoint = Rpc.WebSocketRpcClientFactory.BuildUrl(_owner._host, _owner._port, _owner._path);
                var currentEventMessage = _owner.GetCurrentEventMessage();
                var localPlayerBuffText = _owner.GetLocalPlayerBuffText();
                var inMultiplayerLobby = IsInMultiplayerLobby();
                var matchmakingElapsedSeconds = _owner.GetMatchmakingElapsedSeconds();

                return new DotArenaSceneUiSnapshot
                {
                    HasSession = _owner.HasActiveSession,
                    FlowState = _owner._flowState,
                    EntryMenuState = _owner._entryMenuState,
                    SessionMode = _owner._sessionMode,
                    Status = _owner._status,
                    LocalPlayerId = _owner._localPlayerId,
                    Account = _owner._account,
                    Password = _owner._password,
                    LocalPlayerMassText = _owner.GetLocalPlayerMassText(),
                    LocalWinCount = _owner._localWinCount,
                    LastWorldTick = _owner._lastWorldTick,
                    ViewCount = _owner._views.Count,
                    LocalPlayerBuffText = localPlayerBuffText,
                    DebugPanelVisible = _owner._showDebugPanel,
                    DebugPanelDetail = DotArenaUiTextComposer.BuildDebugPanelDetail(
                        _owner._status,
                        _owner._flowState,
                        _owner._entryMenuState,
                        _owner._sessionMode,
                        _owner._localPlayerId,
                        _owner._lastWorldTick,
                        _owner._views.Count,
                        localPlayerBuffText,
                        currentEventMessage,
                        endpoint,
                        _owner.IsConnected,
                        _owner.IsRealtimeConnected,
                        _owner.IsConnecting),
                    Host = _owner._host,
                    Port = _owner._port,
                    Path = _owner._path,
                    CurrentEventMessage = currentEventMessage,
                    LastRoundRemainingSeconds = _owner._lastRoundRemainingSeconds,
                    MatchRankingEntries = BuildMatchRankingEntries(),
                    MenuLoginStatusText = _owner.GetMenuLoginStatusText(),
                    IsConnecting = _owner.IsConnecting,
                    IsBusy = _owner.IsUiBusy,
                    SettlementTitle = settlementSummary?.Title ?? string.Empty,
                    SettlementDetail = settlementSummary?.Detail ?? string.Empty,
                    SettlementRewardSummary = settlementSummary?.RewardSummary ?? string.Empty,
                    SettlementTaskSummary = settlementSummary?.TaskSummary ?? string.Empty,
                    SettlementNextStepSummary = settlementSummary?.NextStepSummary ?? string.Empty,
                    SettlementPrimaryActionText = settlementSummary == null
                        ? string.Empty
                        : DotArenaUiTextComposer.GetRematchButtonLabel(settlementSummary.SessionMode),
                    MatchmakingTitle = _owner._sessionMode == SessionMode.SinglePlayer
                        ? "准备本地对局"
                        : _owner._flowState == FrontendFlowState.Matchmaking
                            ? "正在排队"
                            : "联机大厅",
                    MatchmakingElapsedSeconds = matchmakingElapsedSeconds,
                    MatchmakingDetail = DotArenaUiTextComposer.BuildMatchmakingDetail(
                        _owner._sessionMode,
                        _owner._currentArenaMapVariant,
                        _owner._currentArenaRuleVariant,
                        _owner._status,
                        currentEventMessage,
                        matchmakingElapsedSeconds,
                        _owner._pendingUiRequest == PendingUiRequest.CancelMatchmaking),
                    MetaPlayerSummary = DotArenaUiTextComposer.BuildMetaPlayerSummary(_owner._metaState, inMultiplayerLobby),
                    MetaLobbyHighlights = DotArenaUiTextComposer.BuildMetaLobbyHighlights(_owner._metaState, inMultiplayerLobby, previewPreset),
                    MetaProfileDetail = DotArenaUiTextComposer.BuildMetaProfileDetail(_owner._metaState, inMultiplayerLobby, previewPreset, _owner._lastRewardSummary, endpoint),
                    MetaTasksDetail = DotArenaUiTextComposer.BuildMetaTasksDetail(_owner._metaState),
                    MetaShopDetail = DotArenaUiTextComposer.BuildMetaShopDetail(_owner._metaState),
                    MetaRecordsDetail = DotArenaUiTextComposer.BuildMetaRecordsDetail(_owner._metaState),
                    MetaLeaderboardDetail = DotArenaUiTextComposer.BuildMetaLeaderboardDetail(_owner._metaState),
                    MetaSettingsDetail = DotArenaUiTextComposer.BuildMetaSettingsDetail(_owner._metaState),
                    MetaFooterHint = DotArenaUiTextComposer.BuildMetaFooterHint(inMultiplayerLobby)
                };
            }

            private List<DotArenaMatchRankingEntry> BuildMatchRankingEntries()
            {
                var rankedStates = new List<KeyValuePair<string, PlayerRenderState>>(_owner._renderStates);
                rankedStates.Sort(static (left, right) =>
                {
                    var massCompare = NormalizeRankingMass(right.Value.Mass).CompareTo(NormalizeRankingMass(left.Value.Mass));
                    if (massCompare != 0)
                    {
                        return massCompare;
                    }

                    return StringComparer.Ordinal.Compare(left.Key, right.Key);
                });

                var entries = new List<DotArenaMatchRankingEntry>(rankedStates.Count);
                for (var i = 0; i < rankedStates.Count; i++)
                {
                    var playerId = rankedStates[i].Key;
                    var renderState = rankedStates[i].Value;
                    entries.Add(new DotArenaMatchRankingEntry(
                        i + 1,
                        playerId,
                        NormalizeRankingMass(renderState.Mass),
                        string.Equals(playerId, _owner._localPlayerId, StringComparison.Ordinal)));
                }

                return entries;
            }

            private static float NormalizeRankingMass(float mass)
            {
                return float.IsNaN(mass) || float.IsInfinity(mass) ? 0f : mass;
            }
        }
    }
}
