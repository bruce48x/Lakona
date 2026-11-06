using System.Security.Cryptography;
using System.Text;
using Server.App.Routing;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.App.Leaderboard;
using Server.App.Rooms;

namespace Server.Hotfix.Users;

[HotfixBehaviorOf(typeof(UserActor))]
public sealed partial class UserBehavior
{
    private readonly PostgresUserStore _userStore;
    private readonly ActorAccess _actors;

    public UserBehavior(PostgresUserStore userStore, ActorAccess actors)
    {
        _userStore = userStore;
        _actors = actors;
    }

    [ActorMethod("login-and-attach")]
    public async ValueTask<UserLoginResult> LoginAndAttachAsync(
        UserActor self,
        UserLoginAndAttachRequest request)
    {
        await EnsureProfileLoadedAsync(self, CancellationToken.None).ConfigureAwait(false);
        var result = await LoginAsync(self, request.Password).ConfigureAwait(false);
        AttachSession(self, result, request);
        return result;
    }

    private async ValueTask<UserLoginResult> LoginAsync(UserActor self, string password)
    {
        var userId = self.Context.Key;
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

        var totals = await _userStore.SaveLoginAsync(new PersistedUser
        {
            UserId = userId,
            PasswordHash = passwordHash,
            LoginCount = self.State.LoginCount,
            CreatedAtUtc = self.State.CreatedAtUtc,
            LastLoginAtUtc = now
        }).ConfigureAwait(false);
        self.State.WinCount = totals.WinCount;
        self.State.VictoryPoints = totals.VictoryPoints;

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

    public ValueTask SetOnlineAsync(UserActor self, UserOnlineStatusRequest request)
    {
        if (self.RecordExists)
        {
            self.State.IsOnline = request.IsOnline;
        }

        return default;
    }

    public async ValueTask AddWinAsync(
        UserActor self,
        UserWinRequest request)
    {
        await ApplySettlementAsync(self, request.SettlementId, winDelta: 1, pointsDelta: 0).ConfigureAwait(false);
    }

    public async ValueTask AddVictoryPointsAsync(
        UserActor self,
        UserVictoryPointsRequest request)
    {
        if (request.Points <= 0)
            return;

        if (await ApplySettlementAsync(self, request.SettlementId, winDelta: 0, pointsDelta: request.Points)
            .ConfigureAwait(false))
        {
            await _actors
                .Startup<LeaderboardActor>(new LeaderboardId(AgarHotfixIds.GlobalLeaderboardActorId))
                .CallAsync(
                    static behavior => behavior.RecordVictoryPointsAsync,
                    new LeaderboardVictoryPointsRequest
                    {
                        PlayerId = self.State.UserId,
                        VictoryPoints = Math.Max(0, self.State.VictoryPoints),
                        WinCount = Math.Max(0, self.State.WinCount)
                    })
                .ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> ApplySettlementAsync(UserActor self, string settlementId, int winDelta, int pointsDelta)
    {
        await EnsureProfileLoadedAsync(self, CancellationToken.None).ConfigureAwait(false);
        if (!self.RecordExists)
            return false;

        var result = await _userStore.ApplySettlementAsync(self.State.UserId, settlementId, winDelta, pointsDelta)
            .ConfigureAwait(false);
        if (result is not { } totals)
            return false;

        self.State.WinCount = totals.WinCount;
        self.State.VictoryPoints = totals.VictoryPoints;
        return totals.Applied;
    }

    public async ValueTask ResetVictoryPointsAsync(
        UserActor self,
        UserVictoryPointsResetRequest request)
    {
        await EnsureProfileLoadedAsync(self, CancellationToken.None).ConfigureAwait(false);
        if (self.RecordExists)
        {
            await _userStore.ResetVictoryPointsAsync(self.State.UserId).ConfigureAwait(false);
            self.State.VictoryPoints = 0;
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

    public ValueTask AttachRealtimeAsync(UserActor self, PlayerRealtimeAttachRequest request)
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

        return default;
    }

    public async ValueTask ClearRealtimeAsync(UserActor self, PlayerRealtimeClearRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (!string.Equals(session.RealtimeSessionId, request.RealtimeSessionId, StringComparison.Ordinal))
        {
            return;
        }

        session.RealtimeSessionId = "";

        var currentRoomId = self.State.Session.CurrentRoomId;
        if (!string.IsNullOrWhiteSpace(currentRoomId))
        {
            try
            {
                await _actors
                    .Route<RoomActor>(new RoomId(currentRoomId))
                    .CallAsync(
                        static behavior => behavior.ClearRealtimeAsync,
                        new RoomRealtimeClearRequest
                        {
                            UserId = userId,
                            RoomId = currentRoomId,
                            RealtimeSessionId = request.RealtimeSessionId,
                            ClearedAtUtc = request.ClearedAtUtc,
                            Reason = request.Reason
                        })
                    .ConfigureAwait(false);
            }
            catch (ActorNotFoundException)
            {
                // The room may already have been released; the user's realtime state is still cleared.
            }
        }
    }

    public ValueTask MarkQueuedAsync(UserActor self, PlayerSessionQueueRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        self.State.UserId = userId;
        var session = self.State.Session;
        session.MatchmakingTicketId = request.TicketId;

        return default;
    }

    public ValueTask ClearQueueAsync(UserActor self, PlayerSessionQueueClearRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        session.MatchmakingTicketId = "";

        return default;
    }

    public ValueTask AssignRoomAsync(UserActor self, PlayerRoomAssignment request)
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

        return default;
    }

    public ValueTask ClearRoomAsync(UserActor self, PlayerRoomClearRequest request)
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

        return default;
    }

    public ValueTask MarkControlResumedAsync(UserActor self, PlayerSessionResumeRequest request)
    {
        var session = self.State.Session;
        if (string.Equals(self.State.UserId, request.UserId, StringComparison.Ordinal) &&
            string.Equals(session.ControlSessionId, request.SessionId, StringComparison.Ordinal))
        {
            session.ConnectionId = request.ConnectionId;
        }
        return default;
    }

    public ValueTask MarkControlDisconnectedAsync(UserActor self, PlayerSessionDisconnectRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        EnsureState(self, userId);

        var session = self.State.Session;
        if (string.IsNullOrWhiteSpace(request.ConnectionId) || string.Equals(session.ConnectionId, request.ConnectionId, StringComparison.Ordinal))
        {
            session.ConnectionId = "";
        }

        return default;
    }

    public ValueTask<PlayerSessionSnapshot> GetSnapshotAsync(UserActor self, PlayerSessionSnapshotRequest request)
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
                UserId = self.Context.Key
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

        var userId = self.Context.Key;
        var persisted = await _userStore
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
