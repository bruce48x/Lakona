using Agar.Sample.State.Contracts;
using Lakona.Game.Server.Actors;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;

namespace Agar.Sample.State.Rooms;

public sealed class RoomActor : Actor
{
    private bool _recordExists;
    private RoomState _state = new();
    public async Task<RoomSettlementResult> CreateAsync(RoomCreateRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var createdAtUtc = NormalizeUtc(request.CreatedAtUtc);
        var maxPlayers = NormalizeRoomSize(request.MaxPlayers);

        if (_recordExists)
        {
            return new RoomSettlementResult
            {
                RoomId = roomId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = _state.WinnerUserId,
                Message = "Room already exists.",
                UpdatedAtUtc = _state.LastUpdatedAtUtc,
                SettlementId = _state.SettlementId,
                Snapshot = BuildSnapshot()
            };
        }

        _state = new RoomState
        {
            RoomId = roomId,
            MatchId = request.MatchId,
            Status = RoomStatus.WaitingForPlayers,
            MaxPlayers = maxPlayers,
            CreatedAtUtc = createdAtUtc,
            LastUpdatedAtUtc = createdAtUtc,
            RuntimeGateway = CloneGateway(request.RuntimeGateway)
        };
        _recordExists = true;

        foreach (var player in request.Players)
        {
            if (!UpsertPlayer(player, createdAtUtc))
            {
                return BuildFailure("Room capacity exceeded while creating the room.", createdAtUtc);
            }
        }

        _state.Revision += 1;

        return new RoomSettlementResult
        {
            RoomId = roomId,
            Succeeded = true,
            AlreadyApplied = false,
            Message = "Room created.",
            UpdatedAtUtc = createdAtUtc,
            Snapshot = BuildSnapshot()
        };
    }

    public async Task<RoomSettlementResult> JoinAsync(PlayerRoomAssignment request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var joinedAtUtc = NormalizeUtc(request.AssignedAtUtc);
        EnsureInitialized(roomId, request.MatchId, joinedAtUtc);

        if (string.IsNullOrWhiteSpace(_state.RoomId))
        {
            return BuildFailure("Room has not been created.", joinedAtUtc);
        }

        if (_state.Status == RoomStatus.Finished)
        {
            return BuildFailure("Room is already finished.", joinedAtUtc);
        }

        if (FindPlayer(request.UserId) is null && _state.Players.Count >= _state.MaxPlayers)
        {
            return BuildFailure("Room is full.", joinedAtUtc);
        }

        if (!UpsertPlayer(request, joinedAtUtc))
        {
            return BuildFailure("Room is full.", joinedAtUtc);
        }
        if (_state.Status == RoomStatus.Created)
        {
            _state.Status = RoomStatus.WaitingForPlayers;
        }

        _state.Revision += 1;
        _state.LastUpdatedAtUtc = joinedAtUtc;

        return BuildSuccess("Player joined the room.", joinedAtUtc);
    }

    public async Task<RoomSettlementResult> LeaveAsync(RoomPlayerLeaveRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var leftAtUtc = NormalizeUtc(request.LeftAtUtc);

        if (!_recordExists)
        {
            return BuildFailure("Room has not been created.", leftAtUtc);
        }

        var player = FindPlayer(request.UserId);
        if (player is null)
        {
            return BuildFailure("Player is not in the room.", leftAtUtc);
        }

        player.IsConnected = false;
        player.IsReady = false;
        player.LeftAtUtc = leftAtUtc;
        player.LeaveReason = request.Reason;
        player.LastSeenAtUtc = leftAtUtc;

        _state.Revision += 1;
        _state.LastUpdatedAtUtc = leftAtUtc;

        return BuildSuccess("Player left the room.", leftAtUtc);
    }

    public async Task<RoomSettlementResult> SetReadyAsync(RoomPlayerReadyRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var updatedAtUtc = NormalizeUtc(request.UpdatedAtUtc);

        if (!_recordExists)
        {
            return BuildFailure("Room has not been created.", updatedAtUtc);
        }

        var player = FindPlayer(request.UserId);
        if (player is null)
        {
            return BuildFailure("Player is not in the room.", updatedAtUtc);
        }

        player.IsReady = request.IsReady;
        player.LastSeenAtUtc = updatedAtUtc;

        _state.Revision += 1;
        _state.LastUpdatedAtUtc = updatedAtUtc;

        return BuildSuccess("Ready state updated.", updatedAtUtc);
    }

    public async Task<RoomSettlementResult> StartAsync(RoomStartRequest request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var startedAtUtc = NormalizeUtc(request.StartedAtUtc);

        if (!_recordExists)
        {
            return BuildFailure("Room has not been created.", startedAtUtc);
        }

        if (_state.Status is RoomStatus.InProgress or RoomStatus.Finished)
        {
            return new RoomSettlementResult
            {
                RoomId = roomId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = _state.WinnerUserId,
                Message = "Room already started or finished.",
                UpdatedAtUtc = _state.LastUpdatedAtUtc,
                SettlementId = _state.SettlementId,
                Snapshot = BuildSnapshot()
            };
        }

        if (_state.Players.Count == 0)
        {
            return BuildFailure("Room has no players.", startedAtUtc);
        }

        _state.Status = RoomStatus.InProgress;
        _state.StartedAtUtc = startedAtUtc;
        _state.LastUpdatedAtUtc = startedAtUtc;
        _state.Revision += 1;

        return BuildSuccess("Room started.", startedAtUtc);
    }

    public async Task<RoomSettlementResult> CompleteAsync(RoomMatchCompletion request)
    {
        var roomId = NormalizeRoomId(request.RoomId);
        var finishedAtUtc = NormalizeUtc(request.FinishedAtUtc);

        if (!_recordExists)
        {
            return BuildFailure("Room has not been created.", finishedAtUtc);
        }

        if (string.IsNullOrWhiteSpace(request.SettlementId))
        {
            return BuildFailure("Settlement id is required for idempotent completion.", finishedAtUtc);
        }

        if (!string.IsNullOrWhiteSpace(_state.SettlementId) &&
            string.Equals(_state.SettlementId, request.SettlementId, StringComparison.Ordinal))
        {
            return new RoomSettlementResult
            {
                RoomId = roomId,
                SettlementId = request.SettlementId,
                Succeeded = true,
                AlreadyApplied = true,
                WinnerUserId = _state.WinnerUserId,
                Message = "Settlement already applied.",
                UpdatedAtUtc = _state.LastUpdatedAtUtc,
                Snapshot = BuildSnapshot()
            };
        }

        _state.Status = RoomStatus.Finished;
        _state.FinishedAtUtc = finishedAtUtc;
        _state.WinnerUserId = request.WinnerUserId;
        _state.SettlementId = request.SettlementId;
        _state.Message = request.Reason;
        _state.LastUpdatedAtUtc = finishedAtUtc;

        foreach (var result in request.Results)
        {
            var player = FindOrCreatePlayer(result.UserId);
            player.Rank = result.Rank;
            player.IsReady = false;
            player.IsConnected = false;
            player.LastSeenAtUtc = finishedAtUtc;
            if (result.IsWinner)
            {
                _state.WinnerUserId = result.UserId;
            }
        }

        _state.Revision += 1;

        return new RoomSettlementResult
        {
            RoomId = roomId,
            SettlementId = request.SettlementId,
            Succeeded = true,
            AlreadyApplied = false,
            WinnerUserId = _state.WinnerUserId,
            Message = "Settlement applied.",
            UpdatedAtUtc = finishedAtUtc,
            Snapshot = BuildSnapshot()
        };
    }

    public Task<RoomSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(BuildSnapshot());
    }

    private void EnsureState(string roomId)
    {
        if (_recordExists)
        {
            if (string.IsNullOrWhiteSpace(_state.RoomId))
            {
                _state.RoomId = roomId;
            }

            return;
        }
    }

    private void EnsureInitialized(string roomId, string matchId, DateTime createdAtUtc)
    {
        if (!_recordExists)
        {
            _state = new RoomState
            {
                RoomId = roomId,
                MatchId = matchId,
                Status = RoomStatus.WaitingForPlayers,
                MaxPlayers = 10,
                CreatedAtUtc = createdAtUtc,
                LastUpdatedAtUtc = createdAtUtc
            };
            _recordExists = true;
        }
    }

    private bool UpsertPlayer(PlayerRoomAssignment request, DateTime joinedAtUtc)
    {
        var existing = FindPlayer(request.UserId);
        if (existing is null)
        {
            if (_state.Players.Count >= _state.MaxPlayers)
            {
                return false;
            }

            existing = new RoomPlayerState
            {
                UserId = request.UserId,
                JoinedAtUtc = joinedAtUtc
            };
            _state.Players.Add(existing);
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

    private RoomPlayerState? FindPlayer(string userId)
    {
        return _state.Players.FirstOrDefault(player => string.Equals(player.UserId, userId, StringComparison.Ordinal));
    }

    private RoomPlayerState FindOrCreatePlayer(string userId)
    {
        var player = FindPlayer(userId);
        if (player is not null)
        {
            return player;
        }

        player = new RoomPlayerState
        {
            UserId = userId,
            JoinedAtUtc = _state.StartedAtUtc == default ? DateTime.UtcNow : _state.StartedAtUtc
        };
        _state.Players.Add(player);
        return player;
    }

    private RoomSnapshot BuildSnapshot()
    {
        var players = _recordExists
            ? _state.Players.Select(player => new RoomPlayerSnapshot
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
        var maxPlayers = _recordExists ? _state.MaxPlayers : 10;

        return new RoomSnapshot
        {
            RoomId = _recordExists ? _state.RoomId : Context.Id.Value,
            MatchId = _recordExists ? _state.MatchId : "",
            Status = _recordExists ? _state.Status : RoomStatus.Created,
            MaxPlayers = maxPlayers,
            CreatedAtUtc = _recordExists ? _state.CreatedAtUtc : default,
            StartedAtUtc = _recordExists ? _state.StartedAtUtc : default,
            FinishedAtUtc = _recordExists ? _state.FinishedAtUtc : default,
            Revision = _recordExists ? _state.Revision : 0,
            Players = players,
            WinnerUserId = _recordExists ? _state.WinnerUserId : "",
            SettlementId = _recordExists ? _state.SettlementId : "",
            LastUpdatedAtUtc = _recordExists ? _state.LastUpdatedAtUtc : default,
            Message = _recordExists ? _state.Message : "",
            MemberCount = memberCount,
            ConnectedCount = connectedCount,
            ReadyCount = readyCount,
            CapacityRemaining = Math.Max(0, maxPlayers - memberCount),
            RuntimeGateway = _recordExists ? CloneGateway(_state.RuntimeGateway) : new GatewayEndpointDescriptor()
        };
    }

    private RoomSettlementResult BuildFailure(string message, DateTime updatedAtUtc)
    {
        return new RoomSettlementResult
        {
            RoomId = Context.Id.Value,
            Succeeded = false,
            AlreadyApplied = false,
            Message = message,
            UpdatedAtUtc = updatedAtUtc,
            Snapshot = BuildSnapshot()
        };
    }

    private RoomSettlementResult BuildSuccess(string message, DateTime updatedAtUtc)
    {
        return new RoomSettlementResult
        {
            RoomId = Context.Id.Value,
            Succeeded = true,
            AlreadyApplied = false,
            Message = message,
            UpdatedAtUtc = updatedAtUtc,
            Snapshot = BuildSnapshot()
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
