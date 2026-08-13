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
    private static FrameSyncStart CreateFrameSyncStart(RoomActor self)
    {
        return new FrameSyncStart
        {
            ProtocolVersion = FrameSyncProtocol.Version,
            RoomId = self.State.RoomId,
            MatchId = self.State.MatchId,
            RandomSeed = FrameSyncProtocol.CreateSeed(self.State.MatchId),
            FixedDeltaSeconds = FrameSyncProtocol.FixedDeltaSeconds,
            MaxPlayers = RoomRules.RoomSize,
            Players = self.State.Players
                .OrderBy(static player => player.SeatIndex)
                .ThenBy(static player => player.UserId, StringComparer.Ordinal)
                .Select(static player => new FrameSyncPlayer
                {
                    PlayerId = player.UserId,
                    SeatIndex = player.SeatIndex
                })
                .ToList()
        };
    }

    private static FrameSyncFrame CreateNextFrame(RoomActor self)
    {
        var frame = new FrameSyncFrame
        {
            MatchId = self.State.MatchId,
            Frame = self.State.LastPublishedFrame + 1
        };

        foreach (var player in self.State.Players
            .OrderBy(static player => player.SeatIndex)
            .ThenBy(static player => player.UserId, StringComparer.Ordinal))
        {
            frame.Inputs.Add(new InputMessage
            {
                PlayerId = player.UserId,
                MoveX = player.IsConnected ? player.InputX : 0f,
                MoveY = player.IsConnected ? player.InputY : 0f,
                ServerTick = frame.Frame,
                AddCheatMass = player.PendingCheatMass
            });
            player.PendingCheatMass = false;
        }

        return frame;
    }

    private async Task CommitSettlementAsync(RoomActor self, FrameSyncMatchResult result, DateTime finishedAtUtc)
    {
        var roomSnapshot = BuildSnapshot(self);
        var settlementId = $"settlement-{self.State.RoomId}-{result.Frame}";
        await CompleteAsync(self, new RoomMatchCompletion
        {
            RoomId = self.State.RoomId,
            SettlementId = settlementId,
            FinishedAtUtc = finishedAtUtc,
            WinnerUserId = result.WinnerPlayerId,
            Reason = "Client frame-sync simulation completed.",
            Results = result.Players.Select(static entry => new RoomSettlementEntry
            {
                UserId = entry.PlayerId,
                Rank = entry.Rank,
                Mass = entry.Mass,
                IsWinner = entry.IsWinner
            }).ToList()
        }).ConfigureAwait(false);

        var roomUserIds = roomSnapshot.Players
            .Select(static player => player.UserId)
            .Where(static playerId => !string.IsNullOrWhiteSpace(playerId))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var userId in roomUserIds)
        {
            await _actors
                .Route<UserActor>(new UserId(userId))
                .CallAsync(
                    static behavior => behavior.ClearRoomAsync,
                    new PlayerRoomClearRequest
                    {
                        UserId = userId,
                        RoomId = self.State.RoomId,
                        ClearedAtUtc = finishedAtUtc,
                        Reason = "Match completed."
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        var winnerEntry = result.Players.FirstOrDefault(entry =>
            entry.IsWinner && roomUserIds.Contains(entry.PlayerId));
        if (winnerEntry is not null)
        {
            await _actors
                .Route<UserActor>(new UserId(winnerEntry.PlayerId))
                .CallAsync(
                    static behavior => behavior.AddWinAsync,
                    new UserWinRequest(),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        foreach (var entry in result.Players
            .Where(entry => roomUserIds.Contains(entry.PlayerId))
            .GroupBy(static entry => entry.PlayerId, StringComparer.Ordinal)
            .Select(static group => group.First()))
        {
            var points = VictoryPointAwards.GetPointsForRank(entry.Rank);
            if (points <= 0)
            {
                continue;
            }

            var userId = new UserId(entry.PlayerId);
            await _actors
                .Route<UserActor>(userId)
                .CallAsync(
                    static behavior => behavior.AddVictoryPointsAsync,
                    new UserVictoryPointsRequest { Points = points },
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

    private static async ValueTask EnsureFrameRelayTimerAsync(RoomActor self, string roomId, CancellationToken cancellationToken)
    {
        if (self.FrameRelayTimerId.IsValid)
        {
            return;
        }

        self.FrameRelayTimerId = await LakonaTimer
            .CreatePeriodicTimerAsync(
                static (BattleRuntimeTimerCallbacks callbacks) => callbacks.TickAsync,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(FrameSyncProtocol.FixedDeltaSeconds),
                new FrameRelayTimerArgs { RoomId = roomId },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask DestroyFrameRelayTimerAsync(RoomActor self)
    {
        var timerId = self.FrameRelayTimerId;
        self.FrameRelayTimerId = default;
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
                CreatedAtUtc = createdAtUtc,
                LastUpdatedAtUtc = createdAtUtc
            };
            self.RecordExists = true;
        }
    }

    private static bool UpsertPlayer(RoomActor self, PlayerRoomAssignment request, DateTime joinedAtUtc)
    {
        var existing = FindPlayer(self, request.UserId);
        if (existing is null)
        {
            if (self.State.Players.Count >= RoomRules.RoomSize)
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
                Rank = player.Rank,
                LastReceivedServerTick = player.LastReceivedServerTick
            }).ToList()
            : [];

        var memberCount = players.Count;
        var connectedCount = players.Count(player => player.IsConnected);
        var readyCount = players.Count(player => player.IsReady);
        var maxPlayers = RoomRules.RoomSize;

        return new RoomSnapshot
        {
            RoomId = self.RecordExists ? self.State.RoomId : self.Context.Key,
            MatchId = self.RecordExists ? self.State.MatchId : "",
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
            RoomId = self.Context.Key,
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
            RoomId = self.Context.Key,
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
