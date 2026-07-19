using Server.App.State.Contracts;
using Server.App.State.Contracts.Leaderboard;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Timers;
using Server.App.State.Contracts.Users;
using Server.App.State.Leaderboard;
using Server.App.State.Rooms;
using Server.App.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Server.Hotfix;
using Server.Hotfix.Gameplay;
using Server.Hotfix.Services;
using Shared.Gameplay;
using Shared.Interfaces;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Users;
using Server.Hotfix.Timers;

namespace Server.Hotfix.State.Rooms;

[HotfixBehaviorOf(typeof(RoomActor))]
public sealed partial class RoomBehavior
{
    private readonly ActorAccess _actors;
    private readonly RoomNotifier _notifier;

    public RoomBehavior(ActorAccess actors, RoomNotifier notifier)
    {
        _actors = actors;
        _notifier = notifier;
    }

    public ValueTask<RoomSettlementResult> CreateAsync(RoomActor self, RoomCreateRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var createdAtUtc = NormalizeUtc(request.CreatedAtUtc);
        var maxPlayers = NormalizeRoomSize(request.MaxPlayers);

        if (self.RecordExists)
        {
            return new ValueTask<RoomSettlementResult>(new RoomSettlementResult
            {
                RoomId = roomId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = self.State.WinnerUserId,
                Message = "Room already exists.",
                UpdatedAtUtc = self.State.LastUpdatedAtUtc,
                SettlementId = self.State.SettlementId,
                Snapshot = BuildSnapshot(self)
            });
        }

        self.State = new RoomState
        {
            RoomId = roomId,
            MatchId = request.MatchId,
            Status = RoomStatus.WaitingForPlayers,
            MaxPlayers = maxPlayers,
            CreatedAtUtc = createdAtUtc,
            LastUpdatedAtUtc = createdAtUtc,
        };
        self.RuntimeSimulation = null;
        self.RecordExists = true;

        foreach (var player in request.Players)
        {
            if (!UpsertPlayer(self, player, createdAtUtc))
            {
                return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room capacity exceeded while creating the room.", createdAtUtc));
            }
        }

        self.State.Revision += 1;

        return new ValueTask<RoomSettlementResult>(new RoomSettlementResult
        {
            RoomId = roomId,
            Succeeded = true,
            AlreadyApplied = false,
            Message = "Room created.",
            UpdatedAtUtc = createdAtUtc,
            Snapshot = BuildSnapshot(self)
        });
    }

    public ValueTask<RoomSettlementResult> JoinAsync(RoomActor self, PlayerRoomAssignment request, CancellationToken cancellationToken = default)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var joinedAtUtc = NormalizeUtc(request.AssignedAtUtc);
        EnsureInitialized(self, roomId, request.MatchId, joinedAtUtc);

        if (string.IsNullOrWhiteSpace(self.State.RoomId))
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room has not been created.", joinedAtUtc));
        }

        if (self.State.Status == RoomStatus.Finished)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room is already finished.", joinedAtUtc));
        }

        if (FindPlayer(self, request.UserId) is null && self.State.Players.Count >= self.State.MaxPlayers)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room is full.", joinedAtUtc));
        }

        if (!UpsertPlayer(self, request, joinedAtUtc))
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room is full.", joinedAtUtc));
        }
        if (self.State.Status == RoomStatus.Created)
        {
            self.State.Status = RoomStatus.WaitingForPlayers;
        }

        self.State.Revision += 1;
        self.State.LastUpdatedAtUtc = joinedAtUtc;

        return new ValueTask<RoomSettlementResult>(BuildSuccess(self, "Player joined the room.", joinedAtUtc));
    }

    public ValueTask<RoomSettlementResult> LeaveAsync(RoomActor self, RoomPlayerLeaveRequest request, CancellationToken cancellationToken = default)
    {
        var leftAtUtc = NormalizeUtc(request.LeftAtUtc);

        if (!self.RecordExists)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room has not been created.", leftAtUtc));
        }

        var player = FindPlayer(self, request.UserId);
        if (player is null)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Player is not in the room.", leftAtUtc));
        }

        player.IsConnected = false;
        player.IsReady = false;
        player.LeftAtUtc = leftAtUtc;
        player.LeaveReason = request.Reason;
        player.LastSeenAtUtc = leftAtUtc;

        self.State.Revision += 1;
        self.State.LastUpdatedAtUtc = leftAtUtc;

        return new ValueTask<RoomSettlementResult>(BuildSuccess(self, "Player left the room.", leftAtUtc));
    }

    public ValueTask<RoomSettlementResult> SetReadyAsync(RoomActor self, RoomPlayerReadyRequest request, CancellationToken cancellationToken = default)
    {
        var updatedAtUtc = NormalizeUtc(request.UpdatedAtUtc);

        if (!self.RecordExists)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room has not been created.", updatedAtUtc));
        }

        var player = FindPlayer(self, request.UserId);
        if (player is null)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Player is not in the room.", updatedAtUtc));
        }

        player.IsReady = request.IsReady;
        player.LastSeenAtUtc = updatedAtUtc;
        if (request.IsReady)
        {
            player.RealtimeSessionId = request.RealtimeSessionId;
            player.IsConnected = true;
        }

        self.State.Revision += 1;
        self.State.LastUpdatedAtUtc = updatedAtUtc;

        return new ValueTask<RoomSettlementResult>(BuildSuccess(self, "Ready state updated.", updatedAtUtc));
    }

    public ValueTask<RoomSettlementResult> ClearRealtimeAsync(RoomActor self, RoomRealtimeClearRequest request, CancellationToken cancellationToken = default)
    {
        var clearedAtUtc = NormalizeUtc(request.ClearedAtUtc);

        if (!self.RecordExists)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room has not been created.", clearedAtUtc));
        }

        var player = FindPlayer(self, request.UserId);
        if (player is null)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Player is not in the room.", clearedAtUtc));
        }

        if (string.Equals(player.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal))
        {
            player.RealtimeSessionId = "";
            player.IsReady = false;
            player.IsConnected = false;
            player.LastSeenAtUtc = clearedAtUtc;
            player.LeaveReason = request.Reason;
            player.LeftAtUtc = clearedAtUtc;
            self.State.Revision += 1;
            self.State.LastUpdatedAtUtc = clearedAtUtc;
        }

        return new ValueTask<RoomSettlementResult>(BuildSuccess(self, "Realtime state updated.", clearedAtUtc));
    }

    public async ValueTask<RoomSettlementResult> StartAsync(RoomActor self, RoomStartRequest request, CancellationToken cancellationToken = default)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var startedAtUtc = NormalizeUtc(request.StartedAtUtc);

        if (!self.RecordExists)
        {
            return BuildFailure(self, "Room has not been created.", startedAtUtc);
        }

        if (self.State.Status == RoomStatus.InProgress)
        {
            await EnsureBattleRuntimeTimerAsync(self, roomId, cancellationToken).ConfigureAwait(false);
            return new RoomSettlementResult
            {
                RoomId = roomId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = self.State.WinnerUserId,
                Message = "Room already started or finished.",
                UpdatedAtUtc = self.State.LastUpdatedAtUtc,
                SettlementId = self.State.SettlementId,
                Snapshot = BuildSnapshot(self)
            };
        }

        if (self.State.Status == RoomStatus.Finished)
        {
            return new RoomSettlementResult
            {
                RoomId = roomId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = self.State.WinnerUserId,
                Message = "Room already started or finished.",
                UpdatedAtUtc = self.State.LastUpdatedAtUtc,
                SettlementId = self.State.SettlementId,
                Snapshot = BuildSnapshot(self)
            };
        }

        if (self.State.Players.Count == 0)
        {
            return BuildFailure(self, "Room has no players.", startedAtUtc);
        }

        self.State.Status = RoomStatus.InProgress;
        self.State.StartedAtUtc = startedAtUtc;
        self.State.LastUpdatedAtUtc = startedAtUtc;
        self.State.Simulation = new ArenaSimulationState();
        self.RuntimeSimulation = null;
        var simulation = GetOrCreateSimulation(self);
        foreach (var player in self.State.Players)
        {
            simulation.UpsertPlayer(new ArenaPlayerRegistration
            {
                PlayerId = player.UserId,
                PreferredSpawnIndex = player.SeatIndex,
                IsBot = false
            });
        }

        self.State.LastWorldState = simulation.CreateWorldState();
        self.State.Revision += 1;
        await EnsureBattleRuntimeTimerAsync(self, roomId, cancellationToken).ConfigureAwait(false);

        return BuildSuccess(self, "Room started.", startedAtUtc);
    }

    public async ValueTask<RoomSettlementResult> CompleteAsync(RoomActor self, RoomMatchCompletion request, CancellationToken cancellationToken = default)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var finishedAtUtc = NormalizeUtc(request.FinishedAtUtc);

        if (!self.RecordExists)
        {
            return BuildFailure(self, "Room has not been created.", finishedAtUtc);
        }

        if (string.IsNullOrWhiteSpace(request.SettlementId))
        {
            return BuildFailure(self, "Settlement id is required for idempotent completion.", finishedAtUtc);
        }

        if (!string.IsNullOrWhiteSpace(self.State.SettlementId) &&
            string.Equals(self.State.SettlementId, request.SettlementId, StringComparison.Ordinal))
        {
            return new RoomSettlementResult
            {
                RoomId = roomId,
                SettlementId = request.SettlementId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = self.State.WinnerUserId,
                Message = "Settlement already applied.",
                UpdatedAtUtc = self.State.LastUpdatedAtUtc,
                Snapshot = BuildSnapshot(self)
            };
        }

        self.State.Status = RoomStatus.Finished;
        self.State.FinishedAtUtc = finishedAtUtc;
        self.State.WinnerUserId = request.WinnerUserId;
        self.State.SettlementId = request.SettlementId;
        self.State.Message = request.Reason;
        self.State.LastUpdatedAtUtc = finishedAtUtc;

        foreach (var result in request.Results)
        {
            var player = FindOrCreatePlayer(self, result.UserId);
            player.Rank = result.Rank;
            player.IsReady = false;
            player.IsConnected = false;
            player.LastSeenAtUtc = finishedAtUtc;
            if (result.IsWinner)
            {
                self.State.WinnerUserId = result.UserId;
            }
        }

        self.State.Revision += 1;
        await DestroyBattleRuntimeTimerAsync(self).ConfigureAwait(false);
        self.RuntimeSimulation = null;

        return new RoomSettlementResult
        {
            RoomId = roomId,
            SettlementId = request.SettlementId,
            Succeeded = true,
            AlreadyApplied = false,
            WinnerUserId = self.State.WinnerUserId,
            Message = "Settlement applied.",
            UpdatedAtUtc = finishedAtUtc,
            Snapshot = BuildSnapshot(self)
        };
    }

    public ValueTask<RoomSnapshot> GetSnapshotAsync(RoomActor self, RoomSnapshotRequest request, CancellationToken cancellationToken = default)
    {
        return new ValueTask<RoomSnapshot>(BuildSnapshot(self));
    }

    public ValueTask SubmitInputAsync(RoomActor self, RoomInputSubmitRequest request, CancellationToken cancellationToken = default)
    {
        if (!self.RecordExists || self.State.Status != RoomStatus.InProgress)
        {
            return default;
        }

        var player = self.State.Players.FirstOrDefault(item => string.Equals(item.UserId, request.UserId, StringComparison.Ordinal));
        if (player is null || !player.IsConnected)
        {
            return default;
        }

        if (string.IsNullOrWhiteSpace(player.RealtimeSessionId) ||
            string.IsNullOrWhiteSpace(request.RealtimeSessionId) ||
            !string.Equals(player.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal))
        {
            return default;
        }

        var simulation = GetOrCreateSimulation(self);
        request.Input.PlayerId = request.UserId;
        simulation.SubmitInput(request.Input);
        self.State.LastUpdatedAtUtc = request.SubmittedAtUtc == default ? DateTime.UtcNow : request.SubmittedAtUtc;
        return default;
    }

    public async ValueTask RunTickAsync(RoomActor self, RoomTickRequest request, CancellationToken cancellationToken = default)
    {
        if (!self.RecordExists || self.State.Status != RoomStatus.InProgress)
        {
            return;
        }

        var deltaSeconds = request.DeltaSeconds <= 0f ? 1f / 20f : request.DeltaSeconds;
        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        var simulation = GetOrCreateSimulation(self);
        var result = simulation.Tick(deltaSeconds);
        self.State.LastWorldState = result.WorldState;
        self.State.LastUpdatedAtUtc = observedAtUtc;
        self.State.Revision += 1;

        var room = BuildSnapshot(self);
        if (self.State.LastPublishedWorldTick != result.WorldState.Tick)
        {
            _notifier.PublishWorldState(room, result.WorldState);
            self.State.LastPublishedWorldTick = result.WorldState.Tick;
        }

        if (self.State.LastPublishedProgressRemainingSeconds != result.WorldState.RoundRemainingSeconds)
        {
            self.State.LastPublishedProgressRemainingSeconds = result.WorldState.RoundRemainingSeconds;
            self.State.ProgressRevision += 1;
            _notifier.PublishMatchProgress(
                room,
                new MatchProgressUpdate
                {
                    MatchId = room.MatchId,
                    RoomId = room.RoomId,
                    ServerTick = result.WorldState.Tick,
                    RoundRemainingSeconds = result.WorldState.RoundRemainingSeconds,
                    ProgressRevision = self.State.ProgressRevision,
                    PublishedAtUtc = observedAtUtc,
                });
        }

        foreach (var death in result.Deaths)
        {
            _notifier.PublishPlayerDead(room, death);
        }

        if (result.MatchEnd is not null)
        {
            _notifier.PublishMatchEnd(room, result.MatchEnd);
        }

        if (result.MatchEnd is not null)
        {
            await CommitSettlementAsync(self, result).ConfigureAwait(false);
        }
    }

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
