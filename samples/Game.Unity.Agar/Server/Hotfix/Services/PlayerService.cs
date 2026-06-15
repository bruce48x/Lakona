using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.App.Realtime;
using Server.App.Services;
using Shared.Interfaces;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Server.Hotfix.Services;

[HotfixService(typeof(IPlayerService))]
public sealed class PlayerService
{
    public async ValueTask<LoginReply> LoginAsync(HotfixServiceCall<LoginRequest, IPlayerCallback> call)
    {
        var req = call.Request;
        var services = PlayerServiceServices.From(call);

        var account = req.Account;
        var password = req.Password;
        if (req.GuestLogin)
        {
            account = CreateGuestAccount();
            password = CreateGuestPassword();
        }

        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginReply { Code = LoginResultCodes.InvalidRequest, Message = "Login request is incomplete." };
        }

        UserLoginResult loginResult;
        try
        {
            loginResult = await services.Users
                .LoginAsync(account, password, req.Reconnect)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            services.Logger.LogWarning(ex, "Login rejected for account {Account}.", account);
            return new LoginReply { Code = LoginResultCodes.Rejected, Message = "Login rejected." };
        }

        GameSessionKey sessionKey;
        if (req.Reconnect)
        {
            var resumeDecision = await services.SessionDirectory
                .ResumeControlAsync(loginResult.UserId, loginResult.SessionToken, call.ConnectionId, call.Callback)
                .ConfigureAwait(false);
            if (resumeDecision.Status != SessionResumeStatus.Resumed || resumeDecision.Session is null)
            {
                return new LoginReply
                {
                    Code = LoginResultCodes.ReconnectStateLost,
                    PlayerId = loginResult.UserId,
                    Account = account,
                    Message = string.IsNullOrWhiteSpace(resumeDecision.Reason)
                        ? "Server session state was lost. Start a new session instead of reconnecting."
                        : resumeDecision.Reason
                };
            }

            sessionKey = resumeDecision.Session.Value;
            await services.Sessions
                .ReconnectAsync(new PlayerSessionReconnectRequest
                {
                    UserId = loginResult.UserId,
                    SessionToken = loginResult.SessionToken,
                    ConnectionId = call.ConnectionId,
                    ReconnectedAtUtc = DateTime.UtcNow,
                    ControlGateway = CloneGateway(services.GatewayNodeIdentity.RealtimeEndpoint)
                })
                .ConfigureAwait(false);
            await services.ReliableMatchmakingPublisher.ReplayPendingAsync(loginResult.UserId).ConfigureAwait(false);
        }
        else
        {
            sessionKey = await services.SessionDirectory
                .RegisterNewControlAsync(loginResult.UserId, loginResult.SessionToken, call.ConnectionId, call.Callback)
                .ConfigureAwait(false);
            await services.Sessions
                .AttachAsync(new PlayerSessionAttachRequest
                {
                    UserId = loginResult.UserId,
                    SessionToken = loginResult.SessionToken,
                    ConnectionId = call.ConnectionId,
                    AttachedAtUtc = DateTime.UtcNow,
                    ControlGateway = CloneGateway(services.GatewayNodeIdentity.RealtimeEndpoint)
                })
            .ConfigureAwait(false);
            await services.ReliablePushOutbox.AckAsync(loginResult.UserId, long.MaxValue).ConfigureAwait(false);
        }

        return new LoginReply
        {
            Code = LoginResultCodes.Ok,
            Token = loginResult.SessionToken,
            PlayerId = loginResult.UserId,
            WinCount = loginResult.WinCount,
            VictoryPoints = loginResult.VictoryPoints,
            Account = account,
            Password = req.GuestLogin ? password : string.Empty,
            SessionId = sessionKey.SessionId,
            SessionGeneration = sessionKey.Generation
        };
    }

    public async ValueTask<LeaderboardReply> GetLeaderboardAsync(HotfixServiceCall<LeaderboardRequest, IPlayerCallback> call)
    {
        var req = call.Request;
        var services = PlayerServiceServices.From(call);
        var topN = req.TopN <= 0 ? 10 : req.TopN;
        var snapshot = await services.Leaderboard
            .GetLeaderboardAsync(topN)
            .ConfigureAwait(false);

        services.Logger.LogInformation("Leaderboard queried. TopN={TopN} Returned={Returned} Period={PeriodStartUtc}.",
            topN,
            snapshot.Entries.Count,
            snapshot.PeriodStartUtc);

        return new LeaderboardReply
        {
            Code = 0,
            PeriodStartUtc = snapshot.PeriodStartUtc,
            SecondsUntilReset = snapshot.SecondsUntilReset,
            Entries = snapshot.Entries.Select(static entry => new Shared.Interfaces.LeaderboardEntry
            {
                PlayerId = entry.PlayerId,
                VictoryPoints = entry.VictoryPoints,
                WinCount = entry.WinCount,
                Rank = entry.Rank
            }).ToList()
        };
    }

    public async ValueTask StartMatchmakingAsync(HotfixServiceCall<MatchmakingRequest, IPlayerCallback> call)
    {
        var services = PlayerServiceServices.From(call);
        var playerId = services.SessionDirectory.GetPlayerIdByConnection(call.ConnectionId);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await services.MatchmakingCoordinator.EnqueueAsync(playerId).ConfigureAwait(false);
    }

    public async ValueTask CancelMatchmakingAsync(HotfixServiceCall<CancelMatchmakingRequest, IPlayerCallback> call)
    {
        var services = PlayerServiceServices.From(call);
        var playerId = services.SessionDirectory.GetPlayerIdByConnection(call.ConnectionId);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await services.MatchmakingCoordinator.CancelAsync(playerId, "Matchmaking cancelled").ConfigureAwait(false);
    }

    public async ValueTask<RealtimeAttachReply> AttachRealtimeAsync(HotfixServiceCall<RealtimeAttachRequest, IPlayerCallback> call)
    {
        var req = call.Request;
        var services = PlayerServiceServices.From(call);
        if (string.IsNullOrWhiteSpace(req.PlayerId) ||
            string.IsNullOrWhiteSpace(req.Token) ||
            string.IsNullOrWhiteSpace(req.RoomId) ||
            string.IsNullOrWhiteSpace(req.MatchId))
        {
            return new RealtimeAttachReply
            {
                Code = 1,
                Message = "Realtime attach request is incomplete."
            };
        }

        var sessionSnapshot = await services.Sessions
            .GetSnapshotAsync(req.PlayerId)
            .ConfigureAwait(false);
        if (!string.Equals(sessionSnapshot.SessionToken, req.Token, StringComparison.Ordinal) ||
            !string.Equals(sessionSnapshot.CurrentRoomId, req.RoomId, StringComparison.Ordinal) ||
            !string.Equals(sessionSnapshot.CurrentMatchId, req.MatchId, StringComparison.Ordinal))
        {
            return new RealtimeAttachReply
            {
                Code = 2,
                Message = "Realtime session attach rejected."
            };
        }

        if (!services.GatewayNodeIdentity.IsRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return new RealtimeAttachReply
            {
                Code = 3,
                Message = "Realtime session must attach to the runtime owner gateway."
            };
        }

        var room = await services.Rooms
            .GetSnapshotAsync(req.RoomId)
            .ConfigureAwait(false);
        await services.RoomRuntimeHost.EnsureRoomReadyAsync(room).ConfigureAwait(false);

        var attached = await services.SessionDirectory
            .AttachRealtimeAsync(req.PlayerId, req.Token, req.RoomId, req.MatchId, call.ConnectionId, call.Callback)
            .ConfigureAwait(false);
        if (!attached)
        {
            return new RealtimeAttachReply
            {
                Code = 2,
                Message = "Realtime session attach rejected."
            };
        }

        return new RealtimeAttachReply
        {
            Code = 0,
            Message = "Realtime session attached.",
            PlayerId = req.PlayerId,
            RoomId = req.RoomId,
            MatchId = req.MatchId
        };
    }

    public async ValueTask<ReliablePushAckReply> AckReliablePushAsync(HotfixServiceCall<ReliablePushAckRequest, IPlayerCallback> call)
    {
        var req = call.Request;
        var services = PlayerServiceServices.From(call);
        var playerId = services.SessionDirectory.GetPlayerIdByConnection(call.ConnectionId);
        if (string.IsNullOrWhiteSpace(playerId) || req.Sequence <= 0)
        {
            return new ReliablePushAckReply
            {
                Code = ReliablePushAckResultCodes.InvalidRequest,
                Message = "Reliable push ack request is incomplete."
            };
        }

        if (!string.IsNullOrWhiteSpace(req.PlayerId) &&
            !string.Equals(req.PlayerId, playerId, StringComparison.Ordinal))
        {
            return new ReliablePushAckReply
            {
                Code = ReliablePushAckResultCodes.InvalidRequest,
                Message = "Reliable push ack player does not match the current session."
            };
        }

        var registration = services.SessionDirectory.Get(playerId);
        if (registration is null)
        {
            return new ReliablePushAckReply
            {
                Code = ReliablePushAckResultCodes.SessionStateLost,
                RequiresNewSession = true,
                Message = "Server session state was lost. Start a new session instead of reconnecting."
            };
        }

        var currentSession = registration.SessionKey;
        var acknowledgedSession = string.IsNullOrWhiteSpace(req.SessionId) || req.SessionGeneration <= 0
            ? currentSession
            : new GameSessionKey(playerId, req.SessionId, req.SessionGeneration);

        if (registration is not null &&
            !string.IsNullOrWhiteSpace(req.Token) &&
            !string.Equals(registration.SessionToken, req.Token, StringComparison.Ordinal))
        {
            return new ReliablePushAckReply
            {
                Code = ReliablePushAckResultCodes.InvalidRequest,
                Message = "Reliable push ack token does not match the current session."
            };
        }

        var outcome = await services.ReliablePushAckService
            .AckAsync(currentSession, acknowledgedSession, req.Sequence)
            .ConfigureAwait(false);

        if (outcome.Status == ReliablePushAckStatus.StateLost)
        {
            return new ReliablePushAckReply
            {
                Code = ReliablePushAckResultCodes.SessionStateLost,
                RequiresNewSession = true,
                Message = "Client acknowledged a reliable push sequence unknown to the server."
            };
        }

        if (outcome.Status == ReliablePushAckStatus.SessionMismatch)
        {
            return new ReliablePushAckReply
            {
                Code = ReliablePushAckResultCodes.SessionStateLost,
                RequiresNewSession = true,
                Message = "Reliable push ack belongs to a different session generation."
            };
        }

        return new ReliablePushAckReply { Code = ReliablePushAckResultCodes.Ok };
    }

    public async ValueTask SubmitInput(HotfixServiceCall<InputMessage, IPlayerCallback> call)
    {
        var req = call.Request;
        var services = PlayerServiceServices.From(call);
        var playerId = services.SessionDirectory.GetPlayerIdByConnection(call.ConnectionId);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(req.PlayerId) &&
            !string.Equals(req.PlayerId, playerId, StringComparison.Ordinal))
        {
            return;
        }

        var sessionSnapshot = await services.Sessions
            .GetSnapshotAsync(playerId)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sessionSnapshot.CurrentRoomId) ||
            !services.GatewayNodeIdentity.IsRuntimeOwner(sessionSnapshot.RuntimeGateway))
        {
            return;
        }

        req.PlayerId = playerId;
        await services.RoomRuntimeHost.SubmitInputAsync(sessionSnapshot.CurrentRoomId, playerId, req).ConfigureAwait(false);
    }

    public async ValueTask LogoutAsync(HotfixServiceCall<LogoutRequest, IPlayerCallback> call)
    {
        var services = PlayerServiceServices.From(call);
        var playerId = services.SessionDirectory.GetPlayerIdByConnection(call.ConnectionId);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await ReleasePlayerAsync(services, playerId, "Logout").ConfigureAwait(false);
    }

    private static async Task ReleasePlayerAsync(PlayerServiceServices services, string playerId, string reason)
    {
        var registration = services.SessionDirectory.Get(playerId);
        try
        {
            await services.MatchmakingCoordinator.ReleasePlayerAsync(playerId, reason).ConfigureAwait(false);
            await services.Sessions
                .MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                {
                    UserId = playerId,
                    ConnectionId = registration?.ConnectionId ?? string.Empty,
                    DisconnectedAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
                .ConfigureAwait(false);
            await services.Users
                .SetOnlineAsync(playerId, false)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            services.Logger.LogError(ex, "Failed to release player {PlayerId} during {Reason}.", playerId, reason);
        }

        if (registration is not null && !string.IsNullOrWhiteSpace(registration.RoomId))
        {
            services.SessionDirectory.ClearRoom(playerId, registration.RoomId);
        }

        services.SessionDirectory.Remove(playerId);
    }

    private static GatewayEndpointDescriptor CloneGateway(GatewayEndpointDescriptor gateway)
    {
        return new GatewayEndpointDescriptor
        {
            InstanceId = gateway.InstanceId,
            Transport = gateway.Transport,
            Host = gateway.Host,
            Port = gateway.Port,
            Path = gateway.Path
        };
    }

    private static string CreateGuestAccount()
    {
        return $"guest-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(6).ToLowerInvariant()}";
    }

    private static string CreateGuestPassword()
    {
        return RandomNumberGenerator.GetHexString(16).ToLowerInvariant();
    }

    private sealed record PlayerServiceServices(
        IUserStateStore Users,
        IPlayerSessionStateStore Sessions,
        IRoomStateStore Rooms,
        ILeaderboardStateStore Leaderboard,
        SessionDirectory SessionDirectory,
        GatewayMatchmakingCoordinator MatchmakingCoordinator,
        GatewayNodeIdentity GatewayNodeIdentity,
        RoomRuntimeHost RoomRuntimeHost,
        ReliableMatchmakingPublisher ReliableMatchmakingPublisher,
        IReliablePushOutbox ReliablePushOutbox,
        IReliablePushAckService ReliablePushAckService,
        ILogger<PlayerService> Logger)
    {
        public static PlayerServiceServices From<TRequest>(HotfixServiceCall<TRequest, IPlayerCallback> call)
        {
            return new PlayerServiceServices(
                call.Services.GetRequiredService<IUserStateStore>(),
                call.Services.GetRequiredService<IPlayerSessionStateStore>(),
                call.Services.GetRequiredService<IRoomStateStore>(),
                call.Services.GetRequiredService<ILeaderboardStateStore>(),
                call.Services.GetRequiredService<SessionDirectory>(),
                call.Services.GetRequiredService<GatewayMatchmakingCoordinator>(),
                call.Services.GetRequiredService<GatewayNodeIdentity>(),
                call.Services.GetRequiredService<RoomRuntimeHost>(),
                call.Services.GetRequiredService<ReliableMatchmakingPublisher>(),
                call.Services.GetRequiredService<IReliablePushOutbox>(),
                call.Services.GetRequiredService<IReliablePushAckService>(),
                call.Services.GetRequiredService<ILogger<PlayerService>>());
        }
    }
}
