using Server.App.State.Contracts;
using Server.App.State.Contracts.Leaderboard;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Lakona.Game.Abstractions;
using Server.App.State.Matchmaking;
using Server.App.State.Leaderboard;
using Server.App.State.Rooms;
using Server.App.State.Users;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Server.Hotfix;
using Server.Hotfix.Services;
using Server.Hotfix.State.Leaderboard;
using Server.Hotfix.State.Matchmaking;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Shared.Interfaces;

namespace Server.Hotfix.Services;

[HotfixService(typeof(IPlayerService))]
public sealed class PlayerService
{
    private readonly LeaderboardActors _leaderboards;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<PlayerService> _logger;
    private readonly MatchmakingActors _matchmaking;
    private readonly RoomActors _rooms;
    private readonly UserActors _users;

    public PlayerService(
        UserActors users,
        RoomActors rooms,
        MatchmakingActors matchmaking,
        LeaderboardActors leaderboards,
        LocalActorNodeIdentity localNode,
        ILogger<PlayerService> logger)
    {
        _users = users;
        _rooms = rooms;
        _matchmaking = matchmaking;
        _leaderboards = leaderboards;
        _localNode = localNode;
        _logger = logger;
    }

    public async ValueTask<LeaderboardReply> GetLeaderboardAsync(HotfixServiceCall<LeaderboardRequest, IPlayerCallback> call)
    {
        var req = call.Request;

        var topN = req.TopN <= 0 ? 10 : req.TopN;
        var leaderboardId = new LeaderboardId(AgarHotfixIds.GlobalLeaderboardActorId);
        var snapshot = await _leaderboards
            .Startup(leaderboardId)
            .CallAsync(
                LeaderboardBehavior.GetLeaderboardAsync,
                new LeaderboardQueryRequest { TopN = topN },
                CancellationToken.None)
            .ConfigureAwait(false);

        _logger.LogInformation("Leaderboard queried. TopN={TopN} Returned={Returned} Period={PeriodStartUtc}.",
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
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await EnqueuePlayerAsync(call.Services, playerId, CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask CancelMatchmakingAsync(HotfixServiceCall<CancelMatchmakingRequest, IPlayerCallback> call)
    {
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await CancelMatchmakingAsync(call.Services, playerId, "Matchmaking cancelled", CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask LogoutAsync(HotfixServiceCall<LogoutRequest, IPlayerCallback> call)
    {
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await ReleasePlayerAsync(call.Services, playerId, "Logout", CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask<string?> EnsureControlConnectionAsync<TRequest, TCallback>(
        HotfixServiceCall<TRequest, TCallback> call)
        where TCallback : class
    {
        if (call.CurrentSession is not { } currentSession)
        {
            return null;
        }

        return currentSession.OwnerKey;
    }

    private Task EnqueuePlayerAsync(IServiceProvider services, string playerId, CancellationToken cancellationToken)
    {
        return EnqueuePlayerAsync(
            _users,
            _matchmaking,
            HotfixNotificationServices.GetMatchmakingNotifier(services),
            playerId,
            cancellationToken);
    }

    private static async Task EnqueuePlayerAsync(
        UserActors users,
        MatchmakingActors matchmaking,
        MatchmakingNotifier matchmakingNotifier,
        string playerId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await users
            .Route(new UserId(playerId))
            .CallAsync(
                UserBehavior.GetSnapshotAsync,
                new PlayerSessionSnapshotRequest(),
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.SessionToken))
        {
            throw new InvalidOperationException($"Player '{playerId}' does not have an attached control session.");
        }

        var result = await matchmaking
            .Startup(new MatchmakingQueueId("default"))
            .CallAsync(
                MatchmakingBehavior.EnqueueAsync,
                new MatchmakingEnqueueRequest
            {
                UserId = playerId,
                SessionToken = snapshot.SessionToken,
                ControlSessionId = snapshot.ControlSessionId,
                ControlSessionGeneration = snapshot.ControlSessionGeneration,
                EnqueuedAtUtc = DateTime.UtcNow
            },
                cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(result.TicketId))
        {
            await users
                .Route(new UserId(playerId))
                .CallAsync(
                    UserBehavior.ClearQueueAsync,
                    new PlayerSessionQueueClearRequest
                {
                    UserId = playerId,
                    QueueId = "default",
                    ClearedAtUtc = DateTime.UtcNow,
                    Reason = result.Matched ? "Matched" : "Matchmaking enqueue did not return a ticket."
                },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await users
                .Route(new UserId(playerId))
                .CallAsync(
                    UserBehavior.MarkQueuedAsync,
                    new PlayerSessionQueueRequest
                {
                    UserId = playerId,
                    QueueId = "default",
                    TicketId = result.TicketId,
                    QueuedAtUtc = DateTime.UtcNow
                },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Matched)
        {
            await PublishMatchedAsync(users, matchmakingNotifier, result.RoomAssignment, cancellationToken).ConfigureAwait(false);
            return;
        }

        await PublishQueuedAsync(matchmakingNotifier, snapshot, result).ConfigureAwait(false);
    }

    private Task CancelMatchmakingAsync(IServiceProvider services, string playerId, string reason, CancellationToken cancellationToken)
    {
        return CancelMatchmakingAsync(
            _users,
            _matchmaking,
            HotfixNotificationServices.GetMatchmakingNotifier(services),
            playerId,
            reason,
            cancellationToken);
    }

    private static async Task CancelMatchmakingAsync(
        UserActors users,
        MatchmakingActors matchmaking,
        MatchmakingNotifier matchmakingNotifier,
        string playerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await users
            .Route(new UserId(playerId))
            .CallAsync(
                UserBehavior.GetSnapshotAsync,
                new PlayerSessionSnapshotRequest(),
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.SessionToken) &&
            string.IsNullOrWhiteSpace(snapshot.MatchmakingTicketId))
        {
            return;
        }

        await matchmaking
            .Startup(new MatchmakingQueueId("default"))
            .CallAsync(
                MatchmakingBehavior.CancelAsync,
                new MatchmakingCancelRequest
            {
                UserId = playerId,
                TicketId = snapshot.MatchmakingTicketId,
                CancelledAtUtc = DateTime.UtcNow,
                Reason = reason
            },
                cancellationToken)
            .ConfigureAwait(false);

        await users
            .Route(new UserId(playerId))
            .CallAsync(
                UserBehavior.ClearQueueAsync,
                new PlayerSessionQueueClearRequest
            {
                UserId = playerId,
                QueueId = string.IsNullOrWhiteSpace(snapshot.QueueId) ? "default" : snapshot.QueueId,
                TicketId = snapshot.MatchmakingTicketId,
                ClearedAtUtc = DateTime.UtcNow,
                Reason = reason
            },
                cancellationToken)
            .ConfigureAwait(false);
        if (!TryCreateControlSession(snapshot, out var controlSession))
        {
            return;
        }

        await matchmakingNotifier.PublishAsync(controlSession, new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Canceled,
            QueueSize = 0,
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(reason) ? "Matchmaking cancelled" : reason
        }).ConfigureAwait(false);
    }

    private Task ReleasePlayerAsync(IServiceProvider services, string playerId, string reason, CancellationToken cancellationToken)
    {
        return ReleasePlayerAsync(
            _users,
            _rooms,
            _matchmaking,
            HotfixNotificationServices.GetMatchmakingNotifier(services),
            _localNode,
            _logger,
            playerId,
            reason,
            cancellationToken);
    }

    internal static async Task ReleasePlayerAsync(
        UserActors users,
        RoomActors rooms,
        MatchmakingActors matchmaking,
        MatchmakingNotifier matchmakingNotifier,
        LocalActorNodeIdentity localNode,
        ILogger<PlayerService> logger,
        string playerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = await users
                .Route(new UserId(playerId))
                .CallAsync(
                    UserBehavior.GetSnapshotAsync,
                    new PlayerSessionSnapshotRequest(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(snapshot.MatchmakingTicketId))
            {
                await CancelMatchmakingAsync(users, matchmaking, matchmakingNotifier, playerId, reason, cancellationToken).ConfigureAwait(false);
                snapshot = await users
                    .Route(new UserId(playerId))
                    .CallAsync(
                        UserBehavior.GetSnapshotAsync,
                        new PlayerSessionSnapshotRequest(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var roomId = snapshot.CurrentRoomId;
            if (!string.IsNullOrWhiteSpace(roomId))
            {
                try
                {
                    await LeaveAssignedRoomAsync(rooms, localNode, snapshot, reason, cancellationToken).ConfigureAwait(false);
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

                await users
                    .Route(new UserId(playerId))
                    .CallAsync(
                        UserBehavior.ClearRoomAsync,
                        new PlayerRoomClearRequest
                    {
                        UserId = playerId,
                        RoomId = roomId,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = reason
                    },
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await users
                .Route(new UserId(playerId))
                .CallAsync(
                    UserBehavior.MarkDisconnectedAsync,
                    new PlayerSessionDisconnectRequest
                {
                    UserId = playerId,
                    ConnectionId = snapshot.ConnectionId,
                    DisconnectedAtUtc = DateTime.UtcNow,
                    Reason = reason
                },
                    cancellationToken)
                .ConfigureAwait(false);
            await users
                .Route(new UserId(playerId))
                .CallAsync(
                    UserBehavior.SetOnlineAsync,
                    new UserOnlineStatusRequest { IsOnline = false },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to release player {PlayerId} during {Reason}.", playerId, reason);
        }
    }

    private static ValueTask<RoomSettlementResult> LeaveAssignedRoomAsync(
        RoomActors rooms,
        LocalActorNodeIdentity localNode,
        PlayerSessionSnapshot snapshot,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var request = new RoomPlayerLeaveRequest
        {
            UserId = snapshot.UserId,
            RoomId = snapshot.CurrentRoomId,
            LeftAtUtc = DateTime.UtcNow,
            Reason = reason
        };
        var roomId = new RoomId(snapshot.CurrentRoomId);
        var localNodeId = localNode.NodeId.Value;

        if (string.IsNullOrWhiteSpace(snapshot.RuntimeGateway.InstanceId) ||
            string.Equals(snapshot.RuntimeGateway.InstanceId, localNodeId, StringComparison.Ordinal))
        {
            return rooms.Local(roomId).CallAsync(RoomBehavior.LeaveAsync, request, cancellationToken);
        }

        return rooms
            .Route(roomId)
            .CallAsync(RoomBehavior.LeaveAsync, request, cancellationToken);
    }

    private static Task PublishQueuedAsync(MatchmakingNotifier matchmakingNotifier, PlayerSessionSnapshot snapshot,
        MatchmakingEnqueueResult result)
    {
        return TryCreateControlSession(snapshot, out var controlSession)
            ? matchmakingNotifier.PublishAsync(controlSession, new MatchmakingStatusUpdate
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

    internal static async Task PublishMatchedAsync(
        UserActors users,
        MatchmakingNotifier matchmakingNotifier,
        RoomAssignment assignment,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assignment.RoomId))
        {
            return;
        }

        foreach (var player in assignment.Players)
        {
            var user = users.Route(new UserId(player.UserId));
            var snapshot = await user
                .CallAsync(
                    UserBehavior.GetSnapshotAsync,
                    new PlayerSessionSnapshotRequest(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(snapshot.SessionToken))
            {
                continue;
            }

            await user
                .CallAsync(
                    UserBehavior.ClearQueueAsync,
                    new PlayerSessionQueueClearRequest
                {
                    UserId = player.UserId,
                    QueueId = string.IsNullOrWhiteSpace(snapshot.QueueId) ? "default" : snapshot.QueueId,
                    TicketId = snapshot.MatchmakingTicketId,
                    ClearedAtUtc = DateTime.UtcNow,
                    Reason = "Matched"
                },
                    cancellationToken)
                .ConfigureAwait(false);
            await user
                .CallAsync(
                    UserBehavior.AssignRoomAsync,
                    new PlayerRoomAssignment
                {
                    UserId = player.UserId,
                    RoomId = assignment.RoomId,
                    MatchId = assignment.MatchId,
                    SeatIndex = player.SeatIndex,
                    SessionToken = snapshot.SessionToken,
                    ConnectionId = snapshot.ConnectionId,
                    AssignedAtUtc = DateTime.UtcNow,
                    RuntimeGateway = assignment.RuntimeGateway
                },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!TryCreateControlSession(snapshot, out var controlSession))
            {
                continue;
            }

            await matchmakingNotifier.PublishAsync(controlSession, new MatchmakingStatusUpdate
            {
                State = Shared.Interfaces.MatchmakingState.Matched,
                QueueSize = assignment.Players.Count,
                RoomCapacity = assignment.MaxPlayers,
                RoomId = assignment.RoomId,
                MatchedPlayerCount = assignment.Players.Count,
                Message = $"Matched into room {assignment.RoomId}",
                RealtimeConnection = RealtimeConnectionMapper.ToRealtimeConnectionInfo(
                    assignment.RuntimeGateway,
                    assignment.RoomId,
                    assignment.MatchId,
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
