using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Rooms;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.State.Rooms;

[HotfixBehaviorOf(typeof(RoomActor))]
public static class RoomBehavior
{
    public static ValueTask<RoomSettlementResult> CreateAsync(this RoomActor self, RoomCreateRequest request)
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
            RuntimeGateway = CloneGateway(request.RuntimeGateway)
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

    public static ValueTask<RoomSettlementResult> JoinAsync(this RoomActor self, PlayerRoomAssignment request)
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

    public static ValueTask<RoomSettlementResult> LeaveAsync(this RoomActor self, RoomPlayerLeaveRequest request)
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

    public static ValueTask<RoomSettlementResult> SetReadyAsync(this RoomActor self, RoomPlayerReadyRequest request)
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

        self.State.Revision += 1;
        self.State.LastUpdatedAtUtc = updatedAtUtc;

        return new ValueTask<RoomSettlementResult>(BuildSuccess(self, "Ready state updated.", updatedAtUtc));
    }

    public static ValueTask<RoomSettlementResult> StartAsync(this RoomActor self, RoomStartRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var startedAtUtc = NormalizeUtc(request.StartedAtUtc);

        if (!self.RecordExists)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room has not been created.", startedAtUtc));
        }

        if (self.State.Status is RoomStatus.InProgress or RoomStatus.Finished)
        {
            return new ValueTask<RoomSettlementResult>(new RoomSettlementResult
            {
                RoomId = roomId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = self.State.WinnerUserId,
                Message = "Room already started or finished.",
                UpdatedAtUtc = self.State.LastUpdatedAtUtc,
                SettlementId = self.State.SettlementId,
                Snapshot = BuildSnapshot(self)
            });
        }

        if (self.State.Players.Count == 0)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room has no players.", startedAtUtc));
        }

        self.State.Status = RoomStatus.InProgress;
        self.State.StartedAtUtc = startedAtUtc;
        self.State.LastUpdatedAtUtc = startedAtUtc;
        self.State.Revision += 1;

        return new ValueTask<RoomSettlementResult>(BuildSuccess(self, "Room started.", startedAtUtc));
    }

    public static ValueTask<RoomSettlementResult> CompleteAsync(this RoomActor self, RoomMatchCompletion request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var finishedAtUtc = NormalizeUtc(request.FinishedAtUtc);

        if (!self.RecordExists)
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Room has not been created.", finishedAtUtc));
        }

        if (string.IsNullOrWhiteSpace(request.SettlementId))
        {
            return new ValueTask<RoomSettlementResult>(BuildFailure(self, "Settlement id is required for idempotent completion.", finishedAtUtc));
        }

        if (!string.IsNullOrWhiteSpace(self.State.SettlementId) &&
            string.Equals(self.State.SettlementId, request.SettlementId, StringComparison.Ordinal))
        {
            return new ValueTask<RoomSettlementResult>(new RoomSettlementResult
            {
                RoomId = roomId,
                SettlementId = request.SettlementId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = self.State.WinnerUserId,
                Message = "Settlement already applied.",
                UpdatedAtUtc = self.State.LastUpdatedAtUtc,
                Snapshot = BuildSnapshot(self)
            });
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

        return new ValueTask<RoomSettlementResult>(new RoomSettlementResult
        {
            RoomId = roomId,
            SettlementId = request.SettlementId,
            Succeeded = true,
            AlreadyApplied = false,
            WinnerUserId = self.State.WinnerUserId,
            Message = "Settlement applied.",
            UpdatedAtUtc = finishedAtUtc,
            Snapshot = BuildSnapshot(self)
        });
    }

    public static ValueTask<RoomSnapshot> GetSnapshotAsync(this RoomActor self)
    {
        return new ValueTask<RoomSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask TickAsync(this RoomActor self, HotfixActorTick tick)
    {
        _ = self;
        _ = tick;
        return default;
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
