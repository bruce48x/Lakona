using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Sessions;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.State.Sessions;

[HotfixBehaviorOf(typeof(PlayerSessionActor))]
public static class PlayerSessionBehavior
{
    public static ValueTask<PlayerSessionSnapshot> AttachAsync(this PlayerSessionActor self, PlayerSessionAttachRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var attachedAtUtc = NormalizeUtc(request.AttachedAtUtc);
        EnsureState(self, userId);

        self.State.UserId = userId;
        self.State.SessionToken = request.SessionToken;
        self.State.ConnectionId = request.ConnectionId;
        self.State.IsOnline = true;
        self.State.IsQueued = false;
        self.State.QueueId = "";
        self.State.MatchmakingTicketId = "";
        self.State.CurrentRoomId = "";
        self.State.CurrentMatchId = "";
        self.State.SeatIndex = -1;
        self.State.AttachedAtUtc = attachedAtUtc;
        self.State.LastConnectedAtUtc = attachedAtUtc;
        self.State.LastHeartbeatAtUtc = attachedAtUtc;
        self.State.ReconnectToken = EnsureReconnectToken(self.State.ReconnectToken);
        self.State.ControlGateway = CloneGateway(request.ControlGateway);
        self.State.RuntimeGateway = new GatewayEndpointDescriptor();

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> ReconnectAsync(this PlayerSessionActor self, PlayerSessionReconnectRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var reconnectedAtUtc = NormalizeUtc(request.ReconnectedAtUtc);
        EnsureState(self, userId);

        self.State.UserId = userId;
        self.State.SessionToken = request.SessionToken;
        self.State.ConnectionId = request.ConnectionId;
        self.State.IsOnline = true;
        self.State.LastConnectedAtUtc = reconnectedAtUtc;
        self.State.LastHeartbeatAtUtc = reconnectedAtUtc;
        self.State.ReconnectToken = EnsureReconnectToken(self.State.ReconnectToken);
        self.State.ControlGateway = CloneGateway(request.ControlGateway);

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> MarkQueuedAsync(this PlayerSessionActor self, PlayerSessionQueueRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var queuedAtUtc = NormalizeUtc(request.QueuedAtUtc);
        EnsureState(self, userId);

        self.State.UserId = userId;
        self.State.IsQueued = true;
        self.State.QueueId = request.QueueId;
        self.State.MatchmakingTicketId = request.TicketId;
        self.State.LastQueuedAtUtc = queuedAtUtc;

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> ClearQueueAsync(this PlayerSessionActor self, PlayerSessionQueueClearRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        self.State.IsQueued = false;
        self.State.QueueId = "";
        self.State.MatchmakingTicketId = "";

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> AssignRoomAsync(this PlayerSessionActor self, PlayerRoomAssignment request)
    {
        var userId = NormalizeUserId(request.UserId);
        var assignedAtUtc = NormalizeUtc(request.AssignedAtUtc);
        EnsureState(self, userId);

        self.State.UserId = userId;
        self.State.SessionToken = string.IsNullOrWhiteSpace(request.SessionToken) ? self.State.SessionToken : request.SessionToken;
        self.State.ConnectionId = string.IsNullOrWhiteSpace(request.ConnectionId) ? self.State.ConnectionId : request.ConnectionId;
        self.State.CurrentRoomId = request.RoomId;
        self.State.CurrentMatchId = request.MatchId;
        self.State.SeatIndex = request.SeatIndex;
        self.State.IsQueued = false;
        self.State.QueueId = "";
        self.State.MatchmakingTicketId = "";
        self.State.IsOnline = true;
        self.State.LastConnectedAtUtc = assignedAtUtc;
        self.State.LastHeartbeatAtUtc = assignedAtUtc;
        self.State.ReconnectToken = EnsureReconnectToken(self.State.ReconnectToken);
        self.State.RuntimeGateway = CloneGateway(request.RuntimeGateway);

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> ClearRoomAsync(this PlayerSessionActor self, PlayerRoomClearRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        if (string.IsNullOrWhiteSpace(request.RoomId) || string.Equals(self.State.CurrentRoomId, request.RoomId, StringComparison.Ordinal))
        {
            self.State.CurrentRoomId = "";
            self.State.CurrentMatchId = "";
            self.State.SeatIndex = -1;
        }

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> MarkDisconnectedAsync(this PlayerSessionActor self, PlayerSessionDisconnectRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var disconnectedAtUtc = NormalizeUtc(request.DisconnectedAtUtc);
        EnsureState(self, userId);

        if (string.IsNullOrWhiteSpace(request.ConnectionId) || string.Equals(self.State.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
        {
            self.State.ConnectionId = "";
        }

        self.State.IsOnline = false;
        self.State.LastDisconnectedAtUtc = disconnectedAtUtc;
        self.State.LastHeartbeatAtUtc = disconnectedAtUtc;

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> HeartbeatAsync(this PlayerSessionActor self, PlayerSessionHeartbeatRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        EnsureState(self, userId);

        self.State.LastHeartbeatAtUtc = observedAtUtc;
        if (self.State.AttachedAtUtc == default)
        {
            self.State.AttachedAtUtc = observedAtUtc;
        }

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(this PlayerSessionActor self)
    {
        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    private static void EnsureState(PlayerSessionActor self, string userId)
    {
        if (!self.RecordExists)
        {
            self.State = new PlayerSessionState
            {
                UserId = userId,
                ReconnectToken = Guid.NewGuid().ToString("N")
            };
            self.RecordExists = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(self.State.UserId) && !string.Equals(self.State.UserId, userId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Player session actor id does not match the requested user id.");
        }
    }

    private static PlayerSessionSnapshot BuildSnapshot(PlayerSessionActor self)
    {
        if (!self.RecordExists)
        {
            return new PlayerSessionSnapshot
            {
                UserId = self.Context.Id.Value
            };
        }

        return new PlayerSessionSnapshot
        {
            UserId = self.State.UserId,
            SessionToken = self.State.SessionToken,
            ConnectionId = self.State.ConnectionId,
            IsOnline = self.State.IsOnline,
            IsQueued = self.State.IsQueued,
            QueueId = self.State.QueueId,
            MatchmakingTicketId = self.State.MatchmakingTicketId,
            CurrentRoomId = self.State.CurrentRoomId,
            CurrentMatchId = self.State.CurrentMatchId,
            SeatIndex = self.State.SeatIndex,
            AttachedAtUtc = self.State.AttachedAtUtc,
            LastQueuedAtUtc = self.State.LastQueuedAtUtc,
            LastConnectedAtUtc = self.State.LastConnectedAtUtc,
            LastDisconnectedAtUtc = self.State.LastDisconnectedAtUtc,
            LastHeartbeatAtUtc = self.State.LastHeartbeatAtUtc,
            ReconnectToken = self.State.ReconnectToken,
            ControlGateway = CloneGateway(self.State.ControlGateway),
            RuntimeGateway = CloneGateway(self.State.RuntimeGateway)
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
