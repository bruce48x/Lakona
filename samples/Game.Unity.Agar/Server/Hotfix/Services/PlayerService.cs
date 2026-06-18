using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Agar.Sample.State;
using Lakona.Game.Abstractions;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.App.Realtime;
using Server.App.Services;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

[HotfixService(typeof(IPlayerService))]
public sealed class PlayerService
{
    public async ValueTask<LeaderboardReply> GetLeaderboardAsync(HotfixServiceCall<LeaderboardRequest, IControlCallback> call)
    {
        var req = call.Request;
        var services = AgarServiceDependencies.From(call);
        var logger = services.CreateLogger<PlayerService>();
        _ = await EnsureControlCallbackBoundAsync(call, services).ConfigureAwait(false);

        var topN = req.TopN <= 0 ? 10 : req.TopN;
        var snapshot = await services.Leaderboard
            .GetLeaderboardAsync(topN)
            .ConfigureAwait(false);

        logger.LogInformation("Leaderboard queried. TopN={TopN} Returned={Returned} Period={PeriodStartUtc}.",
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

    public async ValueTask StartMatchmakingAsync(HotfixServiceCall<MatchmakingRequest, IControlCallback> call)
    {
        var services = AgarServiceDependencies.From(call);
        var playerId = await EnsureControlCallbackBoundAsync(call, services).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await services.MatchmakingCoordinator.EnqueueAsync(playerId).ConfigureAwait(false);
    }

    public async ValueTask CancelMatchmakingAsync(HotfixServiceCall<CancelMatchmakingRequest, IControlCallback> call)
    {
        var services = AgarServiceDependencies.From(call);
        var playerId = await EnsureControlCallbackBoundAsync(call, services).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await services.MatchmakingCoordinator.CancelAsync(playerId, "Matchmaking cancelled").ConfigureAwait(false);
    }

    public async ValueTask<ReliablePushAckReply> AckReliablePushAsync(HotfixServiceCall<ReliablePushAckRequest, IControlCallback> call)
    {
        var req = call.Request;
        var services = AgarServiceDependencies.From(call);
        var playerId = await EnsureControlCallbackBoundAsync(call, services).ConfigureAwait(false);
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

        if (!string.IsNullOrWhiteSpace(req.Token) &&
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

    public async ValueTask LogoutAsync(HotfixServiceCall<LogoutRequest, IControlCallback> call)
    {
        var services = AgarServiceDependencies.From(call);
        var playerId = await EnsureControlCallbackBoundAsync(call, services).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await ReleasePlayerAsync(services, playerId, "Logout").ConfigureAwait(false);
    }

    private static async ValueTask<string?> EnsureControlCallbackBoundAsync<TRequest>(
        HotfixServiceCall<TRequest, IControlCallback> call,
        AgarServiceDependencies services)
    {
        var playerId = services.SessionDirectory.GetPlayerIdByConnection(call.ConnectionId);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return null;
        }

        var newlyBound = await services.SessionDirectory
            .BindControlCallbackAsync(playerId, call.ConnectionId, call.Callback)
            .ConfigureAwait(false);
        if (newlyBound)
        {
            await services.ReliableMatchmakingPublisher
                .ReplayPendingAsync(playerId)
                .ConfigureAwait(false);
        }

        return playerId;
    }

    private static async Task ReleasePlayerAsync(AgarServiceDependencies services, string playerId, string reason)
    {
        var registration = services.SessionDirectory.Get(playerId);
        var logger = services.CreateLogger<PlayerService>();
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
            logger.LogError(ex, "Failed to release player {PlayerId} during {Reason}.", playerId, reason);
        }

        if (registration is not null && !string.IsNullOrWhiteSpace(registration.RoomId))
        {
            services.SessionDirectory.ClearRoom(playerId, registration.RoomId);
        }

        services.SessionDirectory.Remove(playerId);
    }
}

internal sealed record AgarServiceDependencies(
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
    ILoggerFactory LoggerFactory)
{
    public ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.CreateLogger<T>();
    }

    public static AgarServiceDependencies From<TRequest>(HotfixServiceCall<TRequest> call)
    {
        return new AgarServiceDependencies(
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
            call.Services.GetRequiredService<ILoggerFactory>());
    }
}
