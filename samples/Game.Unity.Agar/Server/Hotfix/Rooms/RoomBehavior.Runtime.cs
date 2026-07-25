using Server.App.Routing;
using Server.App.Leaderboard;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Shared.Gameplay;
using Shared.Interfaces;
using Server.Hotfix.Leaderboard;
using Server.Hotfix.Users;

namespace Server.Hotfix.Rooms;

public sealed partial class RoomBehavior
{
    private static ArenaSimulation GetOrCreateSimulation(RoomActor self)
    {
        self.State.Simulation ??= new ArenaSimulationState();
        return self.RuntimeSimulation ??= new ArenaSimulation(new ArenaSimulationOptions
        {
            Arena = ArenaConfig.CreateDefault(),
            RespawnDelaySeconds = 5f,
            TargetParticipantCount = self.State.MaxPlayers,
            MinPlayersToStart = self.State.MaxPlayers,
            EnableBots = true
        }, self.State.Simulation);
    }

    private static MatchEnd CreateMatchEnd(WorldState worldState)
    {
        var winnerPlayerId = worldState.Players
            .OrderByDescending(static player => player.Mass)
            .ThenBy(static player => player.PlayerId, StringComparer.Ordinal)
            .FirstOrDefault()?.PlayerId ?? string.Empty;

        return new MatchEnd
        {
            WinnerPlayerId = winnerPlayerId,
            Tick = worldState.Tick
        };
    }

    private async Task CommitSettlementAsync(RoomActor self, ArenaStepResult result)
    {
        var settlement = ArenaSettlementRules.Settle(result.WorldState);
        var roomSnapshot = BuildSnapshot(self);
        var tick = result.MatchEnd?.Tick ?? result.WorldState.Tick;
        var settlementId = $"settlement-{self.State.RoomId}-{tick}";
        var finishedAtUtc = DateTime.UtcNow;

        await CompleteAsync(self, new RoomMatchCompletion
        {
            RoomId = self.State.RoomId,
            SettlementId = settlementId,
            FinishedAtUtc = finishedAtUtc,
            WinnerUserId = settlement.WinnerPlayerId,
            Reason = settlement.Reason,
            Results = settlement.Entries.Select(entry => new RoomSettlementEntry
            {
                UserId = entry.PlayerId,
                Rank = entry.Rank,
                Mass = entry.Mass,
                IsWinner = entry.IsWinner
            }).ToList()
        }).ConfigureAwait(false);

        var completedUserIds = roomSnapshot.Players
            .Select(static player => player.UserId)
            .Concat(settlement.Entries
                .Where(static entry => !entry.IsBot)
                .Select(static entry => entry.PlayerId))
            .Where(static playerId => !string.IsNullOrWhiteSpace(playerId) && !VictoryPointAwards.IsBotPlayer(playerId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var userId in completedUserIds)
        {
            await _actors
                .Route<UserActor>(new UserId(userId))
                .CallAsync(
                    static behavior => behavior.ClearRoomAsync,
                    new PlayerRoomClearRequest
                    {
                        UserId = userId,
                        RoomId = self.State.RoomId,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = "Match completed."
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        var winnerEntry = settlement.Entries.FirstOrDefault(static entry => entry.IsWinner);
        if (winnerEntry is not null && !winnerEntry.IsBot)
        {
            await _actors
                .Route<UserActor>(new UserId(winnerEntry.PlayerId))
                .CallAsync(
                    static behavior => behavior.AddWinAsync,
                    new UserWinRequest(),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        foreach (var entry in settlement.Entries.Where(static entry => !entry.IsBot && entry.VictoryPoints > 0))
        {
            var userId = new UserId(entry.PlayerId);
            await _actors
                .Route<UserActor>(userId)
                .CallAsync(
                    static behavior => behavior.AddVictoryPointsAsync,
                    new UserVictoryPointsRequest { Points = entry.VictoryPoints },
                    CancellationToken.None)
                .ConfigureAwait(false);
            var profile = await _actors
                .Route<UserActor>(userId)
                .CallAsync(
                    static behavior => behavior.GetProfileAsync,
                    new UserProfileRequest(),
                    CancellationToken.None)
                .ConfigureAwait(false);
            await _actors
                .Startup<LeaderboardActor>(new LeaderboardId(AgarHotfixIds.GlobalLeaderboardActorId))
                .CallAsync(
                    static behavior => behavior.RecordVictoryPointsAsync,
                    new LeaderboardVictoryPointsRequest
                    {
                        PlayerId = entry.PlayerId,
                        VictoryPoints = profile.VictoryPoints,
                        WinCount = profile.WinCount
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask EnsureBattleRuntimeTimerAsync(RoomActor self, string roomId, CancellationToken cancellationToken)
    {
        if (self.BattleRuntimeTimerId.IsValid)
        {
            return;
        }

        self.BattleRuntimeTimerId = await LakonaTimer
            .CreatePeriodicTimerAsync(
                static (BattleRuntimeTimerCallbacks callbacks) => callbacks.TickAsync,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(50),
                new BattleRuntimeTimerArgs { RoomId = roomId },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask DestroyBattleRuntimeTimerAsync(RoomActor self)
    {
        var timerId = self.BattleRuntimeTimerId;
        self.BattleRuntimeTimerId = default;
        if (timerId.IsValid)
        {
            await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void EnsureState(RoomActor self, string roomId)
    {
        if (self.RecordExists && string.IsNullOrWhiteSpace(self.State.RoomId))
        {
            self.State.RoomId = roomId;
        }
    }

    private static void EnsureInitialized(RoomActor self, string roomId, string matchId, DateTime createdAtUtc)
    {
        if (!self.RecordExists)
        {
            self.State = new RoomState
            {
                RoomId = roomId,
                MatchId = matchId,
                Status = RoomStatus.WaitingForPlayers,
                MaxPlayers = 10,
                CreatedAtUtc = createdAtUtc,
                LastUpdatedAtUtc = createdAtUtc
            };
            self.RuntimeSimulation = null;
            self.RecordExists = true;
        }
    }

    private static bool UpsertPlayer(RoomActor self, PlayerRoomAssignment request, DateTime joinedAtUtc)
    {
        var existing = FindPlayer(self, request.UserId);
        if (existing is null)
        {
            if (self.State.Players.Count >= self.State.MaxPlayers)
            {
                return false;
            }

            existing = new RoomPlayerState
            {
                UserId = request.UserId,
                JoinedAtUtc = joinedAtUtc
            };
            self.State.Players.Add(existing);
        }

        existing.SessionToken = request.SessionToken;
        existing.ConnectionId = request.ConnectionId;
        existing.ControlSessionId = request.ControlSessionId;
        existing.SeatIndex = request.SeatIndex;
        existing.IsConnected = true;
        existing.IsReady = false;
        existing.LeftAtUtc = default;
        existing.LeaveReason = "";
        existing.LastSeenAtUtc = joinedAtUtc;
        return true;
    }

    private static RoomPlayerState? FindPlayer(RoomActor self, string userId)
    {
        return self.State.Players.FirstOrDefault(player => string.Equals(player.UserId, userId, StringComparison.Ordinal));
    }

    private static RoomPlayerState FindOrCreatePlayer(RoomActor self, string userId)
    {
        var player = FindPlayer(self, userId);
        if (player is not null)
        {
            return player;
        }

        player = new RoomPlayerState
        {
            UserId = userId,
            JoinedAtUtc = self.State.StartedAtUtc == default ? DateTime.UtcNow : self.State.StartedAtUtc
        };
        self.State.Players.Add(player);
        return player;
    }

    private static RoomSnapshot BuildSnapshot(RoomActor self)
    {
        var players = self.RecordExists
            ? self.State.Players.Select(player => new RoomPlayerSnapshot
            {
                UserId = player.UserId,
                SessionToken = player.SessionToken,
                ConnectionId = player.ConnectionId,
                RealtimeSessionId = player.RealtimeSessionId,
                ControlSessionId = player.ControlSessionId,
                SeatIndex = player.SeatIndex,
                IsReady = player.IsReady,
                IsConnected = player.IsConnected,
                JoinedAtUtc = player.JoinedAtUtc,
                LastSeenAtUtc = player.LastSeenAtUtc,
                LeftAtUtc = player.LeftAtUtc,
                LeaveReason = player.LeaveReason,
                Rank = player.Rank
            }).ToList()
            : [];

        var memberCount = players.Count;
        var connectedCount = players.Count(player => player.IsConnected);
        var readyCount = players.Count(player => player.IsReady);
        var maxPlayers = self.RecordExists ? self.State.MaxPlayers : 10;

        return new RoomSnapshot
        {
            RoomId = self.RecordExists ? self.State.RoomId : self.Context.Id.Value,
            MatchId = self.RecordExists ? self.State.MatchId : "",
            Status = self.RecordExists ? self.State.Status : RoomStatus.Created,
            MaxPlayers = maxPlayers,
            CreatedAtUtc = self.RecordExists ? self.State.CreatedAtUtc : default,
            StartedAtUtc = self.RecordExists ? self.State.StartedAtUtc : default,
            FinishedAtUtc = self.RecordExists ? self.State.FinishedAtUtc : default,
            Revision = self.RecordExists ? self.State.Revision : 0,
            Players = players,
            WinnerUserId = self.RecordExists ? self.State.WinnerUserId : "",
            SettlementId = self.RecordExists ? self.State.SettlementId : "",
            LastUpdatedAtUtc = self.RecordExists ? self.State.LastUpdatedAtUtc : default,
            Message = self.RecordExists ? self.State.Message : "",
            MemberCount = memberCount,
            ConnectedCount = connectedCount,
            ReadyCount = readyCount,
            CapacityRemaining = Math.Max(0, maxPlayers - memberCount),
            RuntimeGateway = self.RecordExists ? CloneGateway(self.State.RuntimeGateway) : new GatewayEndpointDescriptor()
        };
    }

    private static RoomSettlementResult BuildFailure(RoomActor self, string message, DateTime updatedAtUtc)
    {
        return new RoomSettlementResult
        {
            RoomId = self.Context.Id.Value,
            Succeeded = false,
            AlreadyApplied = false,
            Message = message,
            UpdatedAtUtc = updatedAtUtc,
            Snapshot = BuildSnapshot(self)
        };
    }

    private static RoomSettlementResult BuildSuccess(RoomActor self, string message, DateTime updatedAtUtc)
    {
        return new RoomSettlementResult
        {
            RoomId = self.Context.Id.Value,
            Succeeded = true,
            AlreadyApplied = false,
            Message = message,
            UpdatedAtUtc = updatedAtUtc,
            Snapshot = BuildSnapshot(self)
        };
    }

    private static string NormalizeRoomId(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            throw new ArgumentException("Room id is required.", nameof(roomId));
        }

        return roomId;
    }

    private static int NormalizeRoomSize(int requestedSize)
    {
        return Math.Clamp(requestedSize <= 0 ? 10 : requestedSize, 1, 10);
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value == default ? DateTime.UtcNow : value;
    }

    private static GatewayEndpointDescriptor CloneGateway(GatewayEndpointDescriptor? gateway)
    {
        if (gateway is null)
        {
            return new GatewayEndpointDescriptor();
        }

        return new GatewayEndpointDescriptor
        {
            InstanceId = gateway.InstanceId,
            Transport = gateway.Transport,
            Host = gateway.Host,
            Port = gateway.Port,
            Path = gateway.Path
        };
    }
}
