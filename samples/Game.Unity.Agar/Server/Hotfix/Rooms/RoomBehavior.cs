using Server.App.Routing;
using Server.App.Leaderboard;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Server.Hotfix;
using Shared.Gameplay;
using Shared.Interfaces;
using Server.Hotfix.Leaderboard;
using Server.Hotfix.Users;

namespace Server.Hotfix.Rooms;

[HotfixBehaviorOf(typeof(RoomActor))]
public sealed partial class RoomBehavior
{
    private readonly ActorAccess _actors;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly RoomNotifier _notifier;
    private readonly LakonaGameRuntimeOptions _runtime;

    public RoomBehavior(
        ActorAccess actors,
        LocalActorNodeIdentity localNode,
        RoomNotifier notifier,
        LakonaGameRuntimeOptions runtime)
    {
        _actors = actors;
        _localNode = localNode;
        _notifier = notifier;
        _runtime = runtime;
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

        var runtimeGateway = ResolveLocalBattleEndpoint();

        self.State = new RoomState
        {
            RoomId = roomId,
            MatchId = request.MatchId,
            Status = RoomStatus.WaitingForPlayers,
            MaxPlayers = maxPlayers,
            CreatedAtUtc = createdAtUtc,
            LastUpdatedAtUtc = createdAtUtc,
            RuntimeGateway = runtimeGateway,
        };
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

    private GatewayEndpointDescriptor ResolveLocalBattleEndpoint()
    {
        var configured = _runtime.Endpoints.FirstOrDefault(IsBattleEndpoint)
            ?? _runtime.Endpoints.FirstOrDefault(IsLegacyBattleEndpoint);
        if (configured is null || string.IsNullOrWhiteSpace(_localNode.NodeId.Value))
        {
            return new GatewayEndpointDescriptor();
        }

        var advertised = new Uri(configured.ToAdvertisedEndpoint(), UriKind.Absolute);
        return new GatewayEndpointDescriptor
        {
            InstanceId = _localNode.NodeId.Value,
            Transport = configured.Transport,
            Host = advertised.Host,
            Port = advertised.Port,
            Path = advertised.AbsolutePath == "/" ? string.Empty : advertised.AbsolutePath
        };
    }

    private static bool IsBattleEndpoint(LakonaGameEndpointOptions endpoint) =>
        endpoint.RpcServices.Contains("battle", StringComparer.OrdinalIgnoreCase)
        || endpoint.RpcServices.Contains("battle-runtime", StringComparer.OrdinalIgnoreCase);

    private static bool IsLegacyBattleEndpoint(LakonaGameEndpointOptions endpoint) =>
        endpoint.RpcServices.Count == 0
        && string.Equals(endpoint.Transport, "kcp", StringComparison.OrdinalIgnoreCase);

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
            player.InputX = 0f;
            player.InputY = 0f;
            player.PendingCheatMass = false;
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
            await EnsureFrameRelayTimerAsync(self, roomId, cancellationToken).ConfigureAwait(false);
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
        self.State.FrameSyncStart = CreateFrameSyncStart(self);
        self.State.FrameHistory.Clear();
        self.State.LastPublishedFrame = 0;
        self.State.LastPublishedProgressRemainingSeconds = -1;
        self.State.Revision += 1;
        _notifier.PublishFrameSyncStarted(BuildSnapshot(self), self.State.FrameSyncStart);
        await EnsureFrameRelayTimerAsync(self, roomId, cancellationToken).ConfigureAwait(false);

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
            var player = FindPlayer(self, result.UserId);
            if (player is null)
            {
                continue;
            }
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
        await DestroyFrameRelayTimerAsync(self).ConfigureAwait(false);

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

    public ValueTask<RoomFrameSyncSnapshot> GetFrameSyncSnapshotAsync(
        RoomActor self,
        RoomFrameSyncSnapshotRequest request,
        CancellationToken cancellationToken = default)
    {
        return new ValueTask<RoomFrameSyncSnapshot>(new RoomFrameSyncSnapshot
        {
            Start = self.State.FrameSyncStart,
            Frames = self.State.FrameHistory.ToList()
        });
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

        player.InputX = Math.Clamp(request.Input.MoveX, -1f, 1f);
        player.InputY = Math.Clamp(request.Input.MoveY, -1f, 1f);
        player.LastReceivedServerTick = Math.Max(
            player.LastReceivedServerTick,
            Math.Clamp(request.Input.LastReceivedServerTick, 0, self.State.LastPublishedFrame));
        player.PendingCheatMass |= request.Input.AddCheatMass;
        self.State.LastUpdatedAtUtc = request.SubmittedAtUtc == default ? DateTime.UtcNow : request.SubmittedAtUtc;
        return default;
    }

    public async ValueTask SubmitMatchResultAsync(RoomActor self, RoomMatchResultSubmitRequest request, CancellationToken cancellationToken = default)
    {
        if (!self.RecordExists || self.State.Status != RoomStatus.InProgress)
        {
            return;
        }

        var player = FindPlayer(self, request.UserId);
        if (player is null ||
            string.IsNullOrWhiteSpace(player.RealtimeSessionId) ||
            !string.Equals(player.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal) ||
            !string.Equals(request.Result.RoomId, self.State.RoomId, StringComparison.Ordinal) ||
            !string.Equals(request.Result.MatchId, self.State.MatchId, StringComparison.Ordinal) ||
            request.Result.Frame < FrameSyncProtocol.RoundFrameCount ||
            request.Result.Frame > self.State.LastPublishedFrame)
        {
            return;
        }

        await CommitSettlementAsync(
            self,
            request.Result,
            NormalizeUtc(request.SubmittedAtUtc)).ConfigureAwait(false);
    }

    public ValueTask RunFrameAsync(RoomActor self, RoomFrameRequest request, CancellationToken cancellationToken = default)
    {
        if (!self.RecordExists || self.State.Status != RoomStatus.InProgress)
        {
            return default;
        }

        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        var frame = CreateNextFrame(self);
        self.State.FrameHistory.Add(frame);
        if (self.State.FrameHistory.Count > FrameSyncProtocol.MaxReplayFrames)
        {
            self.State.FrameHistory.RemoveAt(0);
        }

        self.State.LastPublishedFrame = frame.Frame;
        self.State.LastUpdatedAtUtc = observedAtUtc;
        self.State.Revision += 1;

        var room = BuildSnapshot(self);
        _notifier.PublishFrames(room, self.State.FrameHistory);

        var remainingSeconds = Math.Max(
            0,
            FrameSyncProtocol.RoundSeconds - (int)MathF.Floor(frame.Frame * FrameSyncProtocol.FixedDeltaSeconds));
        if (self.State.LastPublishedProgressRemainingSeconds != remainingSeconds)
        {
            self.State.LastPublishedProgressRemainingSeconds = remainingSeconds;
            self.State.ProgressRevision += 1;
            _notifier.PublishMatchProgress(
                room,
                new MatchProgressUpdate
                {
                    MatchId = room.MatchId,
                    RoomId = room.RoomId,
                    ServerTick = frame.Frame,
                    RoundRemainingSeconds = remainingSeconds,
                    ProgressRevision = self.State.ProgressRevision,
                    PublishedAtUtc = observedAtUtc,
                });
        }

        return default;
    }
}
