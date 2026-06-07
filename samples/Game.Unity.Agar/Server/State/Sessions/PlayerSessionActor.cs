using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Lakona.Game.Server.Actors;

namespace Agar.Sample.State.Sessions;

public sealed class PlayerSessionActor : Actor
{
    private bool _recordExists;
    private PlayerSessionState _state = new();

    public Task<PlayerSessionSnapshot> AttachAsync(PlayerSessionAttachRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var attachedAtUtc = NormalizeUtc(request.AttachedAtUtc);
        EnsureState(userId);

        _state.UserId = userId;
        _state.SessionToken = request.SessionToken;
        _state.ConnectionId = request.ConnectionId;
        _state.IsOnline = true;
        _state.IsQueued = false;
        _state.QueueId = "";
        _state.MatchmakingTicketId = "";
        _state.CurrentRoomId = "";
        _state.CurrentMatchId = "";
        _state.SeatIndex = -1;
        _state.AttachedAtUtc = attachedAtUtc;
        _state.LastConnectedAtUtc = attachedAtUtc;
        _state.LastHeartbeatAtUtc = attachedAtUtc;
        _state.ReconnectToken = EnsureReconnectToken(_state.ReconnectToken);
        _state.ControlGateway = CloneGateway(request.ControlGateway);
        _state.RuntimeGateway = new GatewayEndpointDescriptor();

        return Task.FromResult(BuildSnapshot());
    }

    public Task<PlayerSessionSnapshot> ReconnectAsync(PlayerSessionReconnectRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var reconnectedAtUtc = NormalizeUtc(request.ReconnectedAtUtc);
        EnsureState(userId);

        _state.UserId = userId;
        _state.SessionToken = request.SessionToken;
        _state.ConnectionId = request.ConnectionId;
        _state.IsOnline = true;
        _state.LastConnectedAtUtc = reconnectedAtUtc;
        _state.LastHeartbeatAtUtc = reconnectedAtUtc;
        _state.ReconnectToken = EnsureReconnectToken(_state.ReconnectToken);
        _state.ControlGateway = CloneGateway(request.ControlGateway);

        return Task.FromResult(BuildSnapshot());
    }

    public Task<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var queuedAtUtc = NormalizeUtc(request.QueuedAtUtc);
        EnsureState(userId);

        _state.UserId = userId;
        _state.IsQueued = true;
        _state.QueueId = request.QueueId;
        _state.MatchmakingTicketId = request.TicketId;
        _state.LastQueuedAtUtc = queuedAtUtc;

        return Task.FromResult(BuildSnapshot());
    }

    public Task<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(userId);

        _state.IsQueued = false;
        _state.QueueId = "";
        _state.MatchmakingTicketId = "";

        return Task.FromResult(BuildSnapshot());
    }

    public Task<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request)
    {
        var userId = NormalizeUserId(request.UserId);
        var assignedAtUtc = NormalizeUtc(request.AssignedAtUtc);
        EnsureState(userId);

        _state.UserId = userId;
        _state.SessionToken = string.IsNullOrWhiteSpace(request.SessionToken) ? _state.SessionToken : request.SessionToken;
        _state.ConnectionId = string.IsNullOrWhiteSpace(request.ConnectionId) ? _state.ConnectionId : request.ConnectionId;
        _state.CurrentRoomId = request.RoomId;
        _state.CurrentMatchId = request.MatchId;
        _state.SeatIndex = request.SeatIndex;
        _state.IsQueued = false;
        _state.QueueId = "";
        _state.MatchmakingTicketId = "";
        _state.IsOnline = true;
        _state.LastConnectedAtUtc = assignedAtUtc;
        _state.LastHeartbeatAtUtc = assignedAtUtc;
        _state.ReconnectToken = EnsureReconnectToken(_state.ReconnectToken);
        _state.RuntimeGateway = CloneGateway(request.RuntimeGateway);

        return Task.FromResult(BuildSnapshot());
    }

    public Task<PlayerSessionSnapshot> ClearRoomAsync(PlayerRoomClearRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(userId);

        if (string.IsNullOrWhiteSpace(request.RoomId) || string.Equals(_state.CurrentRoomId, request.RoomId, StringComparison.Ordinal))
        {
            _state.CurrentRoomId = "";
            _state.CurrentMatchId = "";
            _state.SeatIndex = -1;
        }

        return Task.FromResult(BuildSnapshot());
    }

    public Task<PlayerSessionSnapshot> MarkDisconnectedAsync(PlayerSessionDisconnectRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var disconnectedAtUtc = NormalizeUtc(request.DisconnectedAtUtc);
        EnsureState(userId);

        if (string.IsNullOrWhiteSpace(request.ConnectionId) || string.Equals(_state.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
        {
            _state.ConnectionId = "";
        }

        _state.IsOnline = false;
        _state.LastDisconnectedAtUtc = disconnectedAtUtc;
        _state.LastHeartbeatAtUtc = disconnectedAtUtc;

        return Task.FromResult(BuildSnapshot());
    }

    public Task<PlayerSessionSnapshot> HeartbeatAsync(PlayerSessionHeartbeatRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        EnsureState(userId);

        _state.LastHeartbeatAtUtc = observedAtUtc;
        if (_state.AttachedAtUtc == default)
        {
            _state.AttachedAtUtc = observedAtUtc;
        }

        return Task.FromResult(BuildSnapshot());
    }

    public Task<PlayerSessionSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(BuildSnapshot());
    }

    private void EnsureState(string userId)
    {
        if (!_recordExists)
        {
            _state = new PlayerSessionState
            {
                UserId = userId,
                ReconnectToken = Guid.NewGuid().ToString("N")
            };
            _recordExists = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_state.UserId) && !string.Equals(_state.UserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Player session actor id does not match the requested user id.");
        }
    }

    private PlayerSessionSnapshot BuildSnapshot()
    {
        if (!_recordExists)
        {
            return new PlayerSessionSnapshot
            {
                UserId = Context.Id.Value
            };
        }

        return new PlayerSessionSnapshot
        {
            UserId = _state.UserId,
            SessionToken = _state.SessionToken,
            ConnectionId = _state.ConnectionId,
            IsOnline = _state.IsOnline,
            IsQueued = _state.IsQueued,
            QueueId = _state.QueueId,
            MatchmakingTicketId = _state.MatchmakingTicketId,
            CurrentRoomId = _state.CurrentRoomId,
            CurrentMatchId = _state.CurrentMatchId,
            SeatIndex = _state.SeatIndex,
            AttachedAtUtc = _state.AttachedAtUtc,
            LastQueuedAtUtc = _state.LastQueuedAtUtc,
            LastConnectedAtUtc = _state.LastConnectedAtUtc,
            LastDisconnectedAtUtc = _state.LastDisconnectedAtUtc,
            LastHeartbeatAtUtc = _state.LastHeartbeatAtUtc,
            ReconnectToken = _state.ReconnectToken,
            ControlGateway = CloneGateway(_state.ControlGateway),
            RuntimeGateway = CloneGateway(_state.RuntimeGateway)
        };
    }

    private static string NormalizeUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        return userId;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value == default ? DateTime.UtcNow : value;
    }

    private static string EnsureReconnectToken(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
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
