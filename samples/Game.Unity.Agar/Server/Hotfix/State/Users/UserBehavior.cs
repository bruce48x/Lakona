using System.Security.Cryptography;
using System.Text;
using Server.App.Persistence;
using Server.App.State.Contracts;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Users;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Server.Hotfix.State.Users;

[HotfixBehaviorOf(typeof(UserActor))]
public sealed partial class UserBehavior
{
    private readonly IUserStore _users;

    public UserBehavior(IUserStore users)
    {
        _users = users;
    }

    public async ValueTask<UserLoginResult> LoginAndAttachAsync(
        UserActor self,
        UserLoginAndAttachRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureProfileLoadedAsync(self, cancellationToken).ConfigureAwait(false);
        var result = Login(self, request.Password);
        AttachSession(self, result, request);
        await SaveProfileAsync(self, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private static UserLoginResult Login(UserActor self, string password)
    {
        var userId = self.Context.Id.Value;
        var passwordHash = ComputePasswordHash(password);
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

        self.State.SessionToken = Guid.NewGuid().ToString("N");

        self.State.LoginCount += 1;
        self.State.LastLoginAtUtc = now;
        self.State.IsOnline = true;

        return new UserLoginResult
        {
            UserId = self.State.UserId,
            SessionToken = self.State.SessionToken,
            LoginCount = self.State.LoginCount,
            LastLoginAtUtc = self.State.LastLoginAtUtc,
            WinCount = Math.Max(0, self.State.WinCount),
            VictoryPoints = Math.Max(0, self.State.VictoryPoints)
        };
    }

    public async ValueTask<UserProfileSnapshot> GetProfileAsync(
        UserActor self,
        UserProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureProfileLoadedAsync(self, cancellationToken).ConfigureAwait(false);
        var session = self.State.Session;
        return new UserProfileSnapshot
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
            RealtimeGatewayNodeId = session.RuntimeGateway.InstanceId,
            CurrentRoomId = session.CurrentRoomId,
            CurrentMatchId = session.CurrentMatchId,
            SeatIndex = session.SeatIndex,
            MatchmakingTicketId = session.MatchmakingTicketId
        };
    }

    public ValueTask SetOnlineAsync(UserActor self, UserOnlineStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (self.RecordExists)
        {
            self.State.IsOnline = request.IsOnline;
        }

        return default;
    }

    public async ValueTask AddWinAsync(
        UserActor self,
        UserWinRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureProfileLoadedAsync(self, cancellationToken).ConfigureAwait(false);
        if (self.RecordExists)
        {
            self.State.WinCount = Math.Max(0, self.State.WinCount + 1);
            await SaveProfileAsync(self, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask AddVictoryPointsAsync(
        UserActor self,
        UserVictoryPointsRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureProfileLoadedAsync(self, cancellationToken).ConfigureAwait(false);
        if (self.RecordExists && request.Points > 0)
        {
            self.State.VictoryPoints = Math.Max(0, self.State.VictoryPoints + request.Points);
            await SaveProfileAsync(self, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask ResetVictoryPointsAsync(
        UserActor self,
        UserVictoryPointsResetRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureProfileLoadedAsync(self, cancellationToken).ConfigureAwait(false);
        if (self.RecordExists)
        {
            self.State.VictoryPoints = 0;
            await SaveProfileAsync(self, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void AttachSession(UserActor self, UserLoginResult login, UserLoginAndAttachRequest request)
    {
        var userId = NormalizeUserId(login.UserId);
        EnsureState(self, userId);

        self.State.UserId = userId;
        var session = self.State.Session;
        session.SessionToken = login.SessionToken;
        session.ConnectionId = request.ConnectionId;
        session.ControlSessionId = request.ControlSessionId;
        session.RealtimeSessionId = "";
        session.MatchmakingTicketId = "";
        session.CurrentRoomId = "";
        session.CurrentMatchId = "";
        session.SeatIndex = -1;
        session.RuntimeGateway = new GatewayEndpointDescriptor();
    }

    public ValueTask<PlayerSessionSnapshot> AttachRealtimeAsync(UserActor self, PlayerRealtimeAttachRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (!string.Equals(session.SessionToken, request.SessionToken, StringComparison.Ordinal) ||
            !string.Equals(session.CurrentRoomId, request.RoomId, StringComparison.Ordinal) ||
            !string.Equals(session.CurrentMatchId, request.MatchId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Realtime session attach rejected.");
        }

        session.RealtimeSessionId = request.RealtimeSessionId;

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public ValueTask<PlayerSessionSnapshot> ClearRealtimeAsync(UserActor self, PlayerRealtimeClearRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (string.Equals(session.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal))
        {
            session.RealtimeSessionId = "";
        }

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public ValueTask<PlayerSessionSnapshot> MarkQueuedAsync(UserActor self, PlayerSessionQueueRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        self.State.UserId = userId;
        var session = self.State.Session;
        session.MatchmakingTicketId = request.TicketId;

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public ValueTask<PlayerSessionSnapshot> ClearQueueAsync(UserActor self, PlayerSessionQueueClearRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        session.MatchmakingTicketId = "";

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public ValueTask<PlayerSessionSnapshot> AssignRoomAsync(UserActor self, PlayerRoomAssignment request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        session.SessionToken = string.IsNullOrWhiteSpace(request.SessionToken) ? session.SessionToken : request.SessionToken;
        session.ConnectionId = string.IsNullOrWhiteSpace(request.ConnectionId) ? session.ConnectionId : request.ConnectionId;
        session.CurrentRoomId = request.RoomId;
        session.CurrentMatchId = request.MatchId;
        session.SeatIndex = request.SeatIndex;
        session.MatchmakingTicketId = "";
        session.RuntimeGateway = CloneGateway(request.RuntimeGateway);

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public ValueTask<PlayerSessionSnapshot> ClearRoomAsync(UserActor self, PlayerRoomClearRequest request, CancellationToken cancellationToken = default)
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

    public ValueTask<PlayerSessionSnapshot> MarkDisconnectedAsync(UserActor self, PlayerSessionDisconnectRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (string.IsNullOrWhiteSpace(request.ConnectionId) || string.Equals(session.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
        {
            session.ConnectionId = "";
        }

        return new ValueTask<PlayerSessionSnapshot>(BuildSnapshot(self));
    }

    public ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(UserActor self, PlayerSessionSnapshotRequest request, CancellationToken cancellationToken = default)
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
                UserId = userId
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
            RealtimeSessionId = session.RealtimeSessionId,
            MatchmakingTicketId = session.MatchmakingTicketId,
            CurrentRoomId = session.CurrentRoomId,
            CurrentMatchId = session.CurrentMatchId,
            SeatIndex = session.SeatIndex,
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

    private async ValueTask EnsureProfileLoadedAsync(
        UserActor self,
        CancellationToken cancellationToken)
    {
        if (self.RecordLoaded)
        {
            return;
        }

        var userId = self.Context.Id.Value;
        var persisted = await _users
            .LoadAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        self.RecordLoaded = true;
        if (persisted is null)
        {
            return;
        }

        self.RecordExists = true;
        self.State.UserId = persisted.UserId;
        self.State.PasswordHash = persisted.PasswordHash;
        self.State.LoginCount = persisted.LoginCount;
        self.State.CreatedAtUtc = NormalizeUtc(persisted.CreatedAtUtc);
        self.State.LastLoginAtUtc = NormalizeUtc(persisted.LastLoginAtUtc);
        self.State.WinCount = Math.Max(0, persisted.WinCount);
        self.State.VictoryPoints = Math.Max(0, persisted.VictoryPoints);
    }

    private ValueTask SaveProfileAsync(
        UserActor self,
        CancellationToken cancellationToken)
    {
        return _users.SaveAsync(
            new PersistedUser
            {
                UserId = self.State.UserId,
                PasswordHash = self.State.PasswordHash,
                LoginCount = self.State.LoginCount,
                CreatedAtUtc = NormalizeUtc(self.State.CreatedAtUtc),
                LastLoginAtUtc = NormalizeUtc(self.State.LastLoginAtUtc),
                WinCount = Math.Max(0, self.State.WinCount),
                VictoryPoints = Math.Max(0, self.State.VictoryPoints)
            },
            cancellationToken);
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
