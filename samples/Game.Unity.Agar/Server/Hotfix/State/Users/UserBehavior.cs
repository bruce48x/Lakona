using System.Security.Cryptography;
using System.Text;
using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.State.Users;

[HotfixBehaviorOf(typeof(UserActor))]
public static partial class UserBehavior
{
    public static ValueTask<UserLoginResult> LoginAsync(this UserActor self, UserLoginRequest request, CancellationToken cancellationToken = default)
    {
        var userId = self.Context.Id.Value;
        var passwordHash = ComputePasswordHash(request.Password);
        var now = DateTime.UtcNow;

        if (!self.RecordExists)
        {
            self.State = new UserState
            {
                UserId = userId,
                PasswordHash = passwordHash,
                CreatedAtUtc = now
            };
            self.RecordExists = true;
        }
        else if (!string.Equals(self.State.PasswordHash, passwordHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid password.");
        }

        if (!request.Reconnect || string.IsNullOrWhiteSpace(self.State.SessionToken))
        {
            self.State.SessionToken = Guid.NewGuid().ToString("N");
        }

        self.State.LoginCount += 1;
        self.State.LastLoginAtUtc = now;
        self.State.IsOnline = true;

        return new ValueTask<UserLoginResult>(new UserLoginResult
        {
            UserId = self.State.UserId,
            SessionToken = self.State.SessionToken,
            LoginCount = self.State.LoginCount,
            LastLoginAtUtc = self.State.LastLoginAtUtc,
            WinCount = Math.Max(0, self.State.WinCount),
            VictoryPoints = Math.Max(0, self.State.VictoryPoints)
        });
    }

    public static ValueTask<UserProfileSnapshot> GetProfileAsync(this UserActor self, UserProfileRequest request, CancellationToken cancellationToken = default)
    {
        var session = self.State.Session;
        return new ValueTask<UserProfileSnapshot>(new UserProfileSnapshot
        {
            UserId = self.State.UserId,
            LoginCount = self.State.LoginCount,
            CreatedAtUtc = self.State.CreatedAtUtc,
            LastLoginAtUtc = self.State.LastLoginAtUtc,
            IsOnline = self.State.IsOnline,
            WinCount = Math.Max(0, self.State.WinCount),
            VictoryPoints = Math.Max(0, self.State.VictoryPoints),
            SessionToken = session.SessionToken,
            ControlConnectionId = session.ConnectionId,
            ControlGatewayNodeId = session.ControlGateway.InstanceId,
            RealtimeGatewayNodeId = session.RuntimeGateway.InstanceId,
            CurrentRoomId = session.CurrentRoomId,
            CurrentMatchId = session.CurrentMatchId,
            SeatIndex = session.SeatIndex,
            MatchmakingTicketId = session.MatchmakingTicketId
        });
    }

    public static ValueTask SetOnlineAsync(this UserActor self, UserOnlineStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists)
        {
            self.State.IsOnline = request.IsOnline;
        }

        return default;
    }

    public static ValueTask AddWinAsync(this UserActor self, UserWinRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists)
        {
            self.State.WinCount = Math.Max(0, self.State.WinCount + 1);
        }

        return default;
    }

    public static ValueTask AddVictoryPointsAsync(this UserActor self, UserVictoryPointsRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists && request.Points > 0)
        {
            self.State.VictoryPoints = Math.Max(0, self.State.VictoryPoints + request.Points);
        }

        return default;
    }

    public static ValueTask ResetVictoryPointsAsync(this UserActor self, UserVictoryPointsResetRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists)
        {
            self.State.VictoryPoints = 0;
        }

        return default;
    }

    public static ValueTask<PlayerSessionSnapshot> AttachAsync(this UserActor self, PlayerSessionAttachRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var attachedAtUtc = NormalizeUtc(request.AttachedAtUtc);
        EnsureState(self, userId);

        self.State.UserId = userId;
        var session = self.State.Session;
        session.SessionToken = request.SessionToken;
        session.ConnectionId = request.ConnectionId;
        session.ControlSessionId = request.ControlSessionId;
        session.ControlSessionGeneration = request.ControlSessionGeneration;
        session.RealtimeSessionId = "";
        session.RealtimeSessionGeneration = 0;
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
        session.ControlSessionId = request.ControlSessionId;
        session.ControlSessionGeneration = request.ControlSessionGeneration;
        session.IsOnline = true;
        session.LastConnectedAtUtc = reconnectedAtUtc;
        session.LastHeartbeatAtUtc = reconnectedAtUtc;
        session.ReconnectToken = EnsureReconnectToken(session.ReconnectToken);
        session.ControlGateway = CloneGateway(request.ControlGateway);

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> AttachRealtimeAsync(this UserActor self, PlayerRealtimeAttachRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var attachedAtUtc = NormalizeUtc(request.AttachedAtUtc);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (!string.Equals(session.SessionToken, request.SessionToken, StringComparison.Ordinal) ||
            !string.Equals(session.CurrentRoomId, request.RoomId, StringComparison.Ordinal) ||
            !string.Equals(session.CurrentMatchId, request.MatchId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Realtime session attach rejected.");
        }

        session.RealtimeSessionId = request.RealtimeSessionId;
        session.RealtimeSessionGeneration = request.RealtimeSessionGeneration;
        session.LastConnectedAtUtc = attachedAtUtc;
        session.LastHeartbeatAtUtc = attachedAtUtc;

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public static ValueTask<PlayerSessionSnapshot> ClearRealtimeAsync(this UserActor self, PlayerRealtimeClearRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var clearedAtUtc = NormalizeUtc(request.ClearedAtUtc);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (string.Equals(session.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal) &&
            session.RealtimeSessionGeneration == request.RealtimeSessionGeneration)
        {
            session.RealtimeSessionId = "";
            session.RealtimeSessionGeneration = 0;
            session.LastDisconnectedAtUtc = clearedAtUtc;
            session.LastHeartbeatAtUtc = clearedAtUtc;
        }

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

    public static ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(this UserActor self, PlayerSessionSnapshotRequest request, CancellationToken cancellationToken = default)
    {
        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    private static string ComputePasswordHash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
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
            ControlSessionId = session.ControlSessionId,
            ControlSessionGeneration = session.ControlSessionGeneration,
            RealtimeSessionId = session.RealtimeSessionId,
            RealtimeSessionGeneration = session.RealtimeSessionGeneration,
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
