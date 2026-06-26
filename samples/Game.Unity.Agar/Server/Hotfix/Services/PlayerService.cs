using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Leaderboard;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Contracts.Users;
using Lakona.Game.Abstractions;
using Agar.Sample.State.Matchmaking;
using Agar.Sample.State.Leaderboard;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Users;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.Services;
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
    private readonly LeaderboardActors _leaderboards;
    private readonly MatchmakingActors _matchmaking;
    private readonly RoomActors _rooms;
    private readonly UserActors _users;

    public PlayerService(
        UserActors users,
        RoomActors rooms,
        MatchmakingActors matchmaking,
        LeaderboardActors leaderboards)
    {
        _users = users;
        _rooms = rooms;
        _matchmaking = matchmaking;
        _leaderboards = leaderboards;
    }

    public async ValueTask<LeaderboardReply> GetLeaderboardAsync(HotfixServiceCall<LeaderboardRequest> call)
    {
        var req = call.Request;
        var services = CreateDependencies(call.Services);
        var logger = services.CreateLogger<PlayerService>();
        _ = await EnsureControlConnectionAsync(call, services).ConfigureAwait(false);

        var topN = req.TopN <= 0 ? 10 : req.TopN;
        var snapshot = await _leaderboards
            .Get(new LeaderboardId("current"))
            .GetLeaderboardAsync(new LeaderboardQueryRequest { TopN = topN })
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

    public async ValueTask StartMatchmakingAsync(HotfixServiceCall<MatchmakingRequest> call)
    {
        var services = CreateDependencies(call.Services);
        var playerId = await EnsureControlConnectionAsync(call, services).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await EnqueuePlayerAsync(services, playerId).ConfigureAwait(false);
    }

    public async ValueTask CancelMatchmakingAsync(HotfixServiceCall<CancelMatchmakingRequest> call)
    {
        var services = CreateDependencies(call.Services);
        var playerId = await EnsureControlConnectionAsync(call, services).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await CancelMatchmakingAsync(services, playerId, "Matchmaking cancelled").ConfigureAwait(false);
    }

    public async ValueTask LogoutAsync(HotfixServiceCall<LogoutRequest> call)
    {
        var services = CreateDependencies(call.Services);
        var playerId = await EnsureControlConnectionAsync(call, services).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await ReleasePlayerAsync(services, playerId, "Logout").ConfigureAwait(false);
    }

    private AgarServiceDependencies CreateDependencies(IServiceProvider services)
    {
        return new AgarServiceDependencies(
            _users,
            _rooms,
            _matchmaking,
            _leaderboards,
            services.GetRequiredService<MatchmakingNotifier>(),
            services.GetRequiredService<LocalActorNodeIdentity>(),
            services.GetRequiredService<ILoggerFactory>());
    }

    private static ValueTask<string?> EnsureControlConnectionAsync<TRequest>(
        HotfixServiceCall<TRequest> call,
        AgarServiceDependencies services)
    {
        return new ValueTask<string?>(call.CurrentSession?.OwnerKey);
    }

    internal static async Task EnqueuePlayerAsync(AgarServiceDependencies services, string playerId)
    {
        var snapshot = await services.Users
            .Get(new UserId(playerId))
            .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.SessionToken))
        {
            throw new InvalidOperationException($"Player '{playerId}' does not have an attached control session.");
        }

        var result = await services.Matchmaking
            .Get(new MatchmakingQueueId("default"))
            .EnqueueAsync(new MatchmakingEnqueueRequest
                {
                    UserId = playerId,
                    SessionToken = snapshot.SessionToken,
                    EnqueuedAtUtc = DateTime.UtcNow
                })
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(result.TicketId))
        {
            await services.Users
                .Get(new UserId(playerId))
                .ClearQueueAsync(new PlayerSessionQueueClearRequest
                    {
                        UserId = playerId,
                        QueueId = "default",
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = result.Matched ? "Matched" : "Matchmaking enqueue did not return a ticket."
                    })
                .ConfigureAwait(false);
        }
        else
        {
            await services.Users
                .Get(new UserId(playerId))
                .MarkQueuedAsync(new PlayerSessionQueueRequest
                    {
                        UserId = playerId,
                        QueueId = "default",
                        TicketId = result.TicketId,
                        QueuedAtUtc = DateTime.UtcNow
                    })
                .ConfigureAwait(false);
        }

        if (result.Matched)
        {
            await PublishMatchedAsync(services, result.RoomAssignment).ConfigureAwait(false);
            return;
        }

        await PublishQueuedAsync(services, snapshot, result).ConfigureAwait(false);
    }

    internal static async Task CancelMatchmakingAsync(AgarServiceDependencies services, string playerId, string reason)
    {
        var snapshot = await services.Users
            .Get(new UserId(playerId))
            .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.SessionToken) &&
            string.IsNullOrWhiteSpace(snapshot.MatchmakingTicketId))
        {
            return;
        }

        await services.Matchmaking
            .Get(new MatchmakingQueueId("default"))
            .CancelAsync(new MatchmakingCancelRequest
                {
                    UserId = playerId,
                    TicketId = snapshot.MatchmakingTicketId,
                    CancelledAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
            .ConfigureAwait(false);

        await services.Users
            .Get(new UserId(playerId))
            .ClearQueueAsync(new PlayerSessionQueueClearRequest
                {
                    UserId = playerId,
                    QueueId = string.IsNullOrWhiteSpace(snapshot.QueueId) ? "default" : snapshot.QueueId,
                    TicketId = snapshot.MatchmakingTicketId,
                    ClearedAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
            .ConfigureAwait(false);
        if (!TryCreateControlSession(snapshot, out var controlSession))
        {
            return;
        }

        await services.MatchmakingNotifier.PublishAsync(controlSession, new MatchmakingStatusUpdate
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
        var logger = services.CreateLogger<PlayerService>();
        try
        {
            var snapshot = await services.Users
                .Get(new UserId(playerId))
                .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(snapshot.MatchmakingTicketId))
            {
                await CancelMatchmakingAsync(services, playerId, reason).ConfigureAwait(false);
                snapshot = await services.Users
                    .Get(new UserId(playerId))
                    .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
                    .ConfigureAwait(false);
            }

            var roomId = snapshot.CurrentRoomId;
            if (!string.IsNullOrWhiteSpace(roomId))
            {
                try
                {
                    await LeaveAssignedRoomAsync(services, snapshot, reason).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        ex,
                        "Failed to leave room {RoomId} while releasing player {PlayerId} during {Reason}. Continuing local session cleanup.",
                        roomId,
                        playerId,
                        reason);
                }

                await services.Users
                    .Get(new UserId(playerId))
                    .ClearRoomAsync(new PlayerRoomClearRequest
                        {
                            UserId = playerId,
                            RoomId = roomId,
                            ClearedAtUtc = DateTime.UtcNow,
                            Reason = reason
                        })
                    .ConfigureAwait(false);
            }

            await services.Users
                .Get(new UserId(playerId))
                .MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                    {
                        UserId = playerId,
                        ConnectionId = snapshot.ConnectionId,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = reason
                    })
                .ConfigureAwait(false);
            await services.Users
                .Get(new UserId(playerId))
                .SetOnlineAsync(new UserOnlineStatusRequest { IsOnline = false })
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to release player {PlayerId} during {Reason}.", playerId, reason);
        }
    }

    private static ValueTask<RoomSettlementResult> LeaveAssignedRoomAsync(
        AgarServiceDependencies services,
        PlayerSessionSnapshot snapshot,
        string reason)
    {
        var request = new RoomPlayerLeaveRequest
        {
            UserId = snapshot.UserId,
            RoomId = snapshot.CurrentRoomId,
            LeftAtUtc = DateTime.UtcNow,
            Reason = reason
        };
        var roomId = new RoomId(snapshot.CurrentRoomId);
        var localNode = services.LocalNode.NodeId.Value;

        if (string.IsNullOrWhiteSpace(snapshot.RuntimeGateway.InstanceId) ||
            string.Equals(snapshot.RuntimeGateway.InstanceId, localNode, StringComparison.Ordinal))
        {
            return services.Rooms.Local(roomId).LeaveAsync(request);
        }

        return services.Rooms
            .Remote(new NodeId(snapshot.RuntimeGateway.InstanceId), roomId)
            .LeaveAsync(request);
    }

    internal static Task PublishQueuedAsync(AgarServiceDependencies services, PlayerSessionSnapshot snapshot, MatchmakingEnqueueResult result)
    {
        return TryCreateControlSession(snapshot, out var controlSession)
            ? services.MatchmakingNotifier.PublishAsync(controlSession, new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Queued,
            QueuePosition = result.QueuePosition,
            QueueSize = Math.Max(result.QueuePosition, 1),
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(result.Message) ? "Queued for matchmaking" : result.Message
        }).AsTask()
            : Task.CompletedTask;
    }

    internal static async Task PublishMatchedAsync(AgarServiceDependencies services, RoomAssignment assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment.RoomId))
        {
            return;
        }

        var room = await services.Rooms
            .Get(new RoomId(assignment.RoomId))
            .GetSnapshotAsync(new RoomSnapshotRequest())
            .ConfigureAwait(false);

        foreach (var player in room.Players)
        {
            var snapshot = await services.Users
                .Get(new UserId(player.UserId))
                .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(snapshot.SessionToken))
            {
                continue;
            }

            await services.Users
                .Get(new UserId(player.UserId))
                .ClearQueueAsync(new PlayerSessionQueueClearRequest
                    {
                        UserId = player.UserId,
                        QueueId = string.IsNullOrWhiteSpace(snapshot.QueueId) ? "default" : snapshot.QueueId,
                        TicketId = snapshot.MatchmakingTicketId,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = "Matched"
                    })
                .ConfigureAwait(false);
            await services.Users
                .Get(new UserId(player.UserId))
                .AssignRoomAsync(new PlayerRoomAssignment
                    {
                        UserId = player.UserId,
                        RoomId = room.RoomId,
                        MatchId = room.MatchId,
                        SeatIndex = player.SeatIndex,
                        SessionToken = snapshot.SessionToken,
                        ConnectionId = snapshot.ConnectionId,
                        AssignedAtUtc = DateTime.UtcNow,
                        RuntimeGateway = assignment.RuntimeGateway
                    })
                .ConfigureAwait(false);

            if (!TryCreateControlSession(snapshot, out var controlSession))
            {
                continue;
            }

            await services.MatchmakingNotifier.PublishAsync(controlSession, new MatchmakingStatusUpdate
            {
                State = Shared.Interfaces.MatchmakingState.Matched,
                QueueSize = room.MemberCount > 0 ? room.MemberCount : room.Players.Count,
                RoomCapacity = room.MaxPlayers,
                RoomId = room.RoomId,
                MatchedPlayerCount = room.Players.Count,
                Message = $"Matched into room {room.RoomId}",
                RealtimeConnection = RealtimeConnectionMapper.ToRealtimeConnectionInfo(
                    assignment.RuntimeGateway,
                    room.RoomId,
                    room.MatchId,
                    player.SessionToken)
            }).ConfigureAwait(false);
        }
    }

    private static bool TryCreateControlSession(PlayerSessionSnapshot snapshot, out GameSessionKey controlSession)
    {
        if (string.IsNullOrWhiteSpace(snapshot.UserId) ||
            string.IsNullOrWhiteSpace(snapshot.ControlSessionId) ||
            snapshot.ControlSessionGeneration <= 0)
        {
            controlSession = default;
            return false;
        }

        controlSession = new GameSessionKey(
            snapshot.UserId,
            snapshot.ControlSessionId,
            snapshot.ControlSessionGeneration);
        return true;
    }

}

internal sealed record AgarServiceDependencies(
    UserActors Users,
    RoomActors Rooms,
    MatchmakingActors Matchmaking,
    LeaderboardActors Leaderboards,
    MatchmakingNotifier MatchmakingNotifier,
    LocalActorNodeIdentity LocalNode,
    ILoggerFactory LoggerFactory)
{
    public ILogger<T> CreateLogger<T>()
    {
        return LoggerFactory.CreateLogger<T>();
    }

    public static AgarServiceDependencies From<TRequest>(HotfixServiceCall<TRequest> call)
    {
        return From(call.Services);
    }

    public static AgarServiceDependencies From(IServiceProvider services)
    {
        return new AgarServiceDependencies(
            services.GetRequiredService<UserActors>(),
            services.GetRequiredService<RoomActors>(),
            services.GetRequiredService<MatchmakingActors>(),
            services.GetRequiredService<LeaderboardActors>(),
            services.GetRequiredService<MatchmakingNotifier>(),
            services.GetRequiredService<LocalActorNodeIdentity>(),
            services.GetRequiredService<ILoggerFactory>());
    }
}
