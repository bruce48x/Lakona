using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.State.Sessions;

[HotfixBehaviorOf(typeof(UserActor))]
public static class PlayerSessionBehavior
{
    public static ValueTask<PlayerSessionSnapshot> AttachAsync(this UserActor self, PlayerSessionAttachRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var attachedAtUtc = NormalizeUtc(request.AttachedAtUtc);
        EnsureState(self, userId);

        self.State.UserId = userId;
        var session = self.State.Session;
        session.SessionToken = request.SessionToken;
        session.ConnectionId = request.ConnectionId;
        session.IsOnline = true;
        session.IsQueued = false;
        session.QueueId = "";
        session.MatchmakingTicketId = "";
        session.CurrentRoomId = "";
        session.CurrentMatchId = "";
        session.SeatIndex = -1;
        session.AttachedAtUtc = attachedAtUtc;
        session.LastConnectedAtUtc = attachedAtUtc;
        session.LastHeartbeatAtUtc = attachedAtUtc;
        session.ReconnectToken = EnsureReconnectToken(session.ReconnectToken);
        session.ControlGateway = CloneGateway(request.ControlGateway);
        session.RuntimeGateway = new GatewayEndpointDescriptor();

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> ReconnectAsync(this UserActor self, PlayerSessionReconnectRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var reconnectedAtUtc = NormalizeUtc(request.ReconnectedAtUtc);
        EnsureState(self, userId);

        self.State.UserId = userId;
        var session = self.State.Session;
        session.SessionToken = request.SessionToken;
        session.ConnectionId = request.ConnectionId;
        session.IsOnline = true;
        session.LastConnectedAtUtc = reconnectedAtUtc;
        session.LastHeartbeatAtUtc = reconnectedAtUtc;
        session.ReconnectToken = EnsureReconnectToken(session.ReconnectToken);
        session.ControlGateway = CloneGateway(request.ControlGateway);

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> MarkQueuedAsync(this UserActor self, PlayerSessionQueueRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var queuedAtUtc = NormalizeUtc(request.QueuedAtUtc);
        EnsureState(self, userId);

        self.State.UserId = userId;
        var session = self.State.Session;
        session.IsQueued = true;
        session.QueueId = request.QueueId;
        session.MatchmakingTicketId = request.TicketId;
        session.LastQueuedAtUtc = queuedAtUtc;

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> ClearQueueAsync(this UserActor self, PlayerSessionQueueClearRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        session.IsQueued = false;
        session.QueueId = "";
        session.MatchmakingTicketId = "";

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> AssignRoomAsync(this UserActor self, PlayerRoomAssignment request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var assignedAtUtc = NormalizeUtc(request.AssignedAtUtc);
        EnsureState(self, userId);

        var session = self.State.Session;
        session.SessionToken = string.IsNullOrWhiteSpace(request.SessionToken) ? session.SessionToken : request.SessionToken;
        session.ConnectionId = string.IsNullOrWhiteSpace(request.ConnectionId) ? session.ConnectionId : request.ConnectionId;
        session.CurrentRoomId = request.RoomId;
        session.CurrentMatchId = request.MatchId;
        session.SeatIndex = request.SeatIndex;
        session.IsQueued = false;
        session.QueueId = "";
        session.MatchmakingTicketId = "";
        session.IsOnline = true;
        session.LastConnectedAtUtc = assignedAtUtc;
        session.LastHeartbeatAtUtc = assignedAtUtc;
        session.ReconnectToken = EnsureReconnectToken(session.ReconnectToken);
        session.RuntimeGateway = CloneGateway(request.RuntimeGateway);

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> ClearRoomAsync(this UserActor self, PlayerRoomClearRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (string.IsNullOrWhiteSpace(request.RoomId) || string.Equals(session.CurrentRoomId, request.RoomId, StringComparison.Ordinal))
        {
            session.CurrentRoomId = "";
            session.CurrentMatchId = "";
            session.SeatIndex = -1;
        }

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> MarkDisconnectedAsync(this UserActor self, PlayerSessionDisconnectRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var disconnectedAtUtc = NormalizeUtc(request.DisconnectedAtUtc);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (string.IsNullOrWhiteSpace(request.ConnectionId) || string.Equals(session.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
        {
            session.ConnectionId = "";
        }

        session.IsOnline = false;
        session.LastDisconnectedAtUtc = disconnectedAtUtc;
        session.LastHeartbeatAtUtc = disconnectedAtUtc;

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> HeartbeatAsync(this UserActor self, PlayerSessionHeartbeatRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        EnsureState(self, userId);

        var session = self.State.Session;
        session.LastHeartbeatAtUtc = observedAtUtc;
        if (session.AttachedAtUtc == default)
        {
            session.AttachedAtUtc = observedAtUtc;
        }

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(this UserActor self, PlayerSessionSnapshotRequest request, CancellationToken cancellationToken = default)
    {
        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    private static void EnsureState(UserActor self, string userId)
    {
        if (!self.SessionRecordExists)
        {
            self.State.Session = new PlayerSessionState
            {
                UserId = userId,
                ReconnectToken = Guid.NewGuid().ToString("N")
            };
            self.SessionRecordExists = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(self.State.Session.UserId) && !string.Equals(self.State.Session.UserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Player session actor id does not match the requested user id.");
        }
    }

    private static PlayerSessionSnapshot BuildSnapshot(UserActor self)
    {
        if (!self.SessionRecordExists)
        {
            return new PlayerSessionSnapshot
            {
                UserId = self.Context.Id.Value
            };
        }

        var session = self.State.Session;
        return new PlayerSessionSnapshot
        {
            UserId = session.UserId,
            SessionToken = session.SessionToken,
            ConnectionId = session.ConnectionId,
            IsOnline = session.IsOnline,
            IsQueued = session.IsQueued,
            QueueId = session.QueueId,
            MatchmakingTicketId = session.MatchmakingTicketId,
            CurrentRoomId = session.CurrentRoomId,
            CurrentMatchId = session.CurrentMatchId,
            SeatIndex = session.SeatIndex,
            AttachedAtUtc = session.AttachedAtUtc,
            LastQueuedAtUtc = session.LastQueuedAtUtc,
            LastConnectedAtUtc = session.LastConnectedAtUtc,
            LastDisconnectedAtUtc = session.LastDisconnectedAtUtc,
            LastHeartbeatAtUtc = session.LastHeartbeatAtUtc,
            ReconnectToken = session.ReconnectToken,
            ControlGateway = CloneGateway(session.ControlGateway),
            RuntimeGateway = CloneGateway(session.RuntimeGateway)
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
