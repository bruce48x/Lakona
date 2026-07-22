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
                var currentEventMessage = _owner.GetCurrentEventMessage();
                var inMultiplayerLobby = IsInMultiplayerLobby();
                var matchmakingElapsedSeconds = _owner.GetMatchmakingElapsedSeconds();
                var leaderboardEntries = BuildLeaderboardEntries();

                return new DotArenaSceneUiSnapshot
                {
                    HasSession = _owner.HasActiveSession,
                    FlowState = _owner._flowState,
                    EntryMenuState = _owner._entryMenuState,
                    SessionMode = _owner._sessionMode,
                    Status = _owner._status,
                    Account = _owner._account,
                    Password = _owner._password,
                    LastRoundRemainingSeconds = _owner._lastRoundRemainingSeconds,
                    MatchRankingEntries = BuildMatchRankingEntries(),
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
                        ? "Preparing local match"
                        : _owner._flowState == FrontendFlowState.Matchmaking
                            ? "Queued"
                            : "Multiplayer lobby",
                    MatchmakingElapsedSeconds = matchmakingElapsedSeconds,
                    MatchmakingDetail = DotArenaUiTextComposer.BuildMatchmakingDetail(
                        _owner._sessionMode,
                        _owner._currentArenaMapVariant,
                        _owner._currentArenaRuleVariant,
                        _owner._status,
                        currentEventMessage,
                        matchmakingElapsedSeconds,
                        _owner._pendingUiRequest == PendingUiRequest.CancelMatchmaking),
                    ProfilePlayerId = _owner._authenticatedPlayerId,
                    ProfileWinCount = Math.Max(0, _owner._localWinCount),
                    ProfileVictoryPoints = Math.Max(0, _owner._localVictoryPoints),
                    LeaderboardPeriodStartUtc = _owner._metaState?.LeaderboardPeriodStartUtc ?? string.Empty,
                    LeaderboardSecondsUntilReset = Math.Max(0, _owner._metaState?.LeaderboardSecondsUntilReset ?? 0),
                    LeaderboardEntries = leaderboardEntries
                };
            }

            private List<DotArenaLeaderboardUiEntry> BuildLeaderboardEntries()
            {
                var source = _owner._metaState?.LeaderboardEntries;
                var entries = new List<DotArenaLeaderboardUiEntry>(source?.Count ?? 0);
                if (source == null)
                {
                    return entries;
                }

                foreach (var entry in source)
                {
                    entries.Add(new DotArenaLeaderboardUiEntry(
                        entry.Position,
                        entry.Name,
                        entry.VictoryPoints,
                        entry.Wins,
                        entry.IsLocalPlayer));
                }

                return entries;
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
                        string.Equals(playerId, _owner._localPlayerId, StringComparison.Ordinal)
                            ? "You"
                            : $"Player {i + 1}",
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
