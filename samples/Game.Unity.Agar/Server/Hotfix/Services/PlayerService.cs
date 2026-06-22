using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Lakona.Game.Abstractions;
using Agar.Sample.State.Matchmaking;
using Agar.Sample.State.Leaderboard;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Sessions;
using Agar.Sample.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.App.Services;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Matchmaking;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Sessions;
using Server.Hotfix.State.Users;
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
        var nodeLocalActors = call.Actors;
        _ = await EnsureControlCallbackBoundAsync(call, services).ConfigureAwait(false);

        var topN = req.TopN <= 0 ? 10 : req.TopN;
        var snapshot = await nodeLocalActors
            .AskAsync<LeaderboardActor, LeaderboardSnapshot>(
                LeaderboardId,
                (actor, _) => actor.GetLeaderboardAsync(topN))
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

        await EnqueuePlayerAsync(services, playerId).ConfigureAwait(false);
    }

    public async ValueTask CancelMatchmakingAsync(HotfixServiceCall<CancelMatchmakingRequest, IControlCallback> call)
    {
        var services = AgarServiceDependencies.From(call);
        var playerId = await EnsureControlCallbackBoundAsync(call, services).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await CancelMatchmakingAsync(services, playerId, "Matchmaking cancelled").ConfigureAwait(false);
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

    internal static async Task EnqueuePlayerAsync(AgarServiceDependencies services, string playerId)
    {
        var registration = services.SessionDirectory.Get(playerId)
            ?? throw new InvalidOperationException($"Player '{playerId}' is not registered.");

        var result = await services.LocalActors
            .AskAsync<MatchmakingActor, MatchmakingEnqueueResult>(
                DefaultQueueId,
                (actor, _) => actor.EnqueueAsync(new MatchmakingEnqueueRequest
                {
                    UserId = playerId,
                    SessionToken = registration.SessionToken,
                    EnqueuedAtUtc = DateTime.UtcNow
                }))
            .ConfigureAwait(false);

        services.SessionDirectory.SetQueueTicket(playerId, string.IsNullOrWhiteSpace(result.TicketId) ? null : result.TicketId);

        if (result.Matched)
        {
            await PublishMatchedAsync(services, result.RoomAssignment).ConfigureAwait(false);
            return;
        }

        await PublishQueuedAsync(services, playerId, result).ConfigureAwait(false);
    }

    internal static async Task CancelMatchmakingAsync(AgarServiceDependencies services, string playerId, string reason)
    {
        var registration = services.SessionDirectory.Get(playerId);
        if (registration is null)
        {
            return;
        }

        await services.LocalActors
            .AskAsync<MatchmakingActor, MatchmakingCancelResult>(
                DefaultQueueId,
                (actor, _) => actor.CancelAsync(new MatchmakingCancelRequest
                {
                    UserId = playerId,
                    TicketId = registration.MatchmakingTicketId ?? string.Empty,
                    CancelledAtUtc = DateTime.UtcNow,
                    Reason = reason
                }))
            .ConfigureAwait(false);

        services.SessionDirectory.SetQueueTicket(playerId, null);
        await services.ReliableMatchmakingPublisher.PublishAsync(playerId, new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Canceled,
            QueueSize = 0,
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(reason) ? "Matchmaking cancelled" : reason
        }).ConfigureAwait(false);
    }

    internal static async Task ReleasePlayerAsync(AgarServiceDependencies services, string playerId, string reason)
    {
        var registration = services.SessionDirectory.Get(playerId);
        var logger = services.CreateLogger<PlayerService>();
        try
        {
            if (registration is not null && !string.IsNullOrWhiteSpace(registration.MatchmakingTicketId))
            {
                await CancelMatchmakingAsync(services, playerId, reason).ConfigureAwait(false);
            }

            var roomId = registration?.RoomId;
            if (!string.IsNullOrWhiteSpace(roomId))
            {
                await services.LocalActors
                    .AskAsync<RoomActor, RoomSettlementResult>(
                        RoomId(roomId),
                        (actor, _) => actor.LeaveAsync(new RoomPlayerLeaveRequest
                        {
                            UserId = playerId,
                            RoomId = roomId,
                            LeftAtUtc = DateTime.UtcNow,
                            Reason = reason
                        }))
                    .ConfigureAwait(false);
                await services.LocalActors
                    .AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
                        SessionId(playerId),
                        (actor, _) => actor.ClearRoomAsync(new PlayerRoomClearRequest
                        {
                            UserId = playerId,
                            RoomId = roomId,
                            ClearedAtUtc = DateTime.UtcNow,
                            Reason = reason
                        }))
                    .ConfigureAwait(false);
                services.SessionDirectory.ClearRoom(playerId, roomId);
            }
            else
            {
                services.SessionDirectory.ClearRoom(playerId);
            }

            await services.LocalActors
                .AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
                    SessionId(playerId),
                    (actor, _) => actor.MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                    {
                        UserId = playerId,
                        ConnectionId = registration?.ConnectionId ?? string.Empty,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = reason
                    }))
                .ConfigureAwait(false);
            await services.LocalActors
                .TellAsync<UserActor>(
                    UserId(playerId),
                    (actor, _) => actor.SetOnlineAsync(false))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to release player {PlayerId} during {Reason}.", playerId, reason);
        }

        services.SessionDirectory.Remove(playerId);
    }

    internal static Task PublishQueuedAsync(AgarServiceDependencies services, string playerId, MatchmakingEnqueueResult result)
    {
        return services.ReliableMatchmakingPublisher.PublishAsync(playerId, new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Queued,
            QueuePosition = result.QueuePosition,
            QueueSize = Math.Max(result.QueuePosition, 1),
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(result.Message) ? "Queued for matchmaking" : result.Message
        }).AsTask();
    }

    internal static async Task PublishMatchedAsync(AgarServiceDependencies services, RoomAssignment assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment.RoomId))
        {
            return;
        }

        var room = await services.LocalActors
            .AskAsync<RoomActor, RoomSnapshot>(
                RoomId(assignment.RoomId),
                (actor, _) => actor.GetSnapshotAsync())
            .ConfigureAwait(false);

        foreach (var player in room.Players)
        {
            var registration = services.SessionDirectory.Get(player.UserId);
            if (registration is null)
            {
                continue;
            }

            services.SessionDirectory.SetQueueTicket(player.UserId, null);
            services.SessionDirectory.AssignRoom(player.UserId, room.RoomId, room.MatchId, player.SeatIndex);

            await services.ReliableMatchmakingPublisher.PublishAsync(player.UserId, new MatchmakingStatusUpdate
            {
                State = Shared.Interfaces.MatchmakingState.Matched,
                QueueSize = room.MemberCount > 0 ? room.MemberCount : room.Players.Count,
                RoomCapacity = room.MaxPlayers,
                RoomId = room.RoomId,
                MatchedPlayerCount = room.Players.Count,
                Message = $"Matched into room {room.RoomId}",
                RealtimeConnection = RealtimeEndpointMapper.ToRealtimeConnectionInfo(
                    assignment.RuntimeGateway,
                    room.RoomId,
                    room.MatchId,
                    player.SessionToken)
            }).ConfigureAwait(false);
        }
    }

    private static readonly ActorId LeaderboardId = ActorId.From("current");
    private static readonly ActorId DefaultQueueId = ActorId.From("default");

    private static ActorId UserId(string userId) => ActorId.From(userId);

    private static ActorId SessionId(string userId) => ActorId.From($"session:{userId}");

    private static ActorId RoomId(string roomId) => ActorId.From(roomId);
}

internal sealed record AgarServiceDependencies(
    IActorRuntime LocalActors,
    SessionDirectory SessionDirectory,
    GatewayNodeIdentity GatewayNodeIdentity,
    BattleRuntimeGatewayResolver RuntimeGateways,
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
        return From(call.Services, call.Actors);
    }

    public static AgarServiceDependencies From(IServiceProvider services, IActorRuntime localActors)
    {
        return new AgarServiceDependencies(
            localActors,
            services.GetRequiredService<SessionDirectory>(),
            services.GetRequiredService<GatewayNodeIdentity>(),
            services.GetRequiredService<BattleRuntimeGatewayResolver>(),
            services.GetRequiredService<ReliableMatchmakingPublisher>(),
            services.GetRequiredService<IReliablePushOutbox>(),
            services.GetRequiredService<IReliablePushAckService>(),
            services.GetRequiredService<ILoggerFactory>());
    }
}
