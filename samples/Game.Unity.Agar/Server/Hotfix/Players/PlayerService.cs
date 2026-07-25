using Server.App.Routing;
using Server.App.Leaderboard;
using Server.App.Matchmaking;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Abstractions;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Server.App.Generated;
using Server.Hotfix;
using Server.Hotfix.Leaderboard;
using Server.Hotfix.Matchmaking;
using Server.Hotfix.Rooms;
using Server.Hotfix.Sessions;
using Server.Hotfix.Users;
using Shared.Interfaces;

namespace Server.Hotfix.Players;

[HotfixService(typeof(IPlayerService))]
public sealed class PlayerService
{
    private readonly ActorAccess _actors;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly ILogger<PlayerService> _logger;
    private readonly MatchmakingNotifier _matchmakingNotifier;

    public PlayerService(
        ActorAccess actors,
        LocalActorNodeIdentity localNode,
        ILogger<PlayerService> logger,
        MatchmakingNotifier matchmakingNotifier)
    {
        _actors = actors;
        _localNode = localNode;
        _logger = logger;
        _matchmakingNotifier = matchmakingNotifier;
    }

    public async ValueTask<LeaderboardReply> GetLeaderboardAsync(
        PlayerServiceCall<LeaderboardRequest> call)
    {
        var req = call.Request;

        var topN = req.TopN <= 0 ? 10 : req.TopN;
        var leaderboardId = new LeaderboardId(AgarHotfixIds.GlobalLeaderboardActorId);
        var snapshot = await _actors
            .Startup<LeaderboardActor>(leaderboardId)
            .CallAsync(
                static behavior => behavior.GetLeaderboardAsync,
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
            Entries = snapshot.Entries
        };
    }

    public async ValueTask StartMatchmakingAsync(PlayerServiceCall<MatchmakingRequest> call)
    {
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await EnqueuePlayerAsync(playerId, CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask CancelMatchmakingAsync(PlayerServiceCall<CancelMatchmakingRequest> call)
    {
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await CancelMatchmakingAsync(playerId, "Matchmaking cancelled", CancellationToken.None)
            .ConfigureAwait(false);
    }

    public async ValueTask LogoutAsync(PlayerServiceCall<LogoutRequest> call)
    {
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await ReleasePlayerAsync(playerId, "Logout", CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask<string?> EnsureControlConnectionAsync<TRequest>(
        PlayerServiceCall<TRequest> call)
    {
        if (call.CurrentSession is not { } currentSession)
        {
            return null;
        }

        return currentSession.OwnerKey;
    }

    private Task EnqueuePlayerAsync(string playerId, CancellationToken cancellationToken)
    {
        return EnqueuePlayerAsync(
            _actors,
            _matchmakingNotifier,
            playerId,
            cancellationToken);
    }

    private static async Task EnqueuePlayerAsync(
        ActorAccess actors,
        MatchmakingNotifier matchmakingNotifier,
        string playerId,
        CancellationToken cancellationToken = default)
    {
        var userId = new UserId(playerId);
        var snapshot = await actors
            .Route<UserActor>(userId)
            .CallAsync(
                static behavior => behavior.GetSnapshotAsync,
                new PlayerSessionSnapshotRequest(),
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.SessionToken))
        {
            throw new InvalidOperationException($"Player '{playerId}' does not have an attached control session.");
        }

        var result = await actors
            .Startup<MatchmakingActor>(new MatchmakingQueueId("default"))
            .CallAsync(
                static behavior => behavior.EnqueueAsync,
                new MatchmakingEnqueueRequest
                {
                    UserId = playerId,
                    SessionToken = snapshot.SessionToken,
                    ControlSessionId = snapshot.ControlSessionId,
                    EnqueuedAtUtc = DateTime.UtcNow
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(result.TicketId))
        {
            await actors
                .Route<UserActor>(userId)
                .CallAsync(
                    static behavior => behavior.ClearQueueAsync,
                    new PlayerSessionQueueClearRequest
                    {
                        UserId = playerId,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = result.Matched ? "Matched" : "Matchmaking enqueue did not return a ticket."
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await actors
                .Route<UserActor>(userId)
                .CallAsync(
                    static behavior => behavior.MarkQueuedAsync,
                    new PlayerSessionQueueRequest
                    {
                        UserId = playerId,
                        TicketId = result.TicketId,
                        QueuedAtUtc = DateTime.UtcNow
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Matched)
        {
            await PublishMatchedAsync(actors, matchmakingNotifier, result.RoomAssignment, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        PublishQueued(matchmakingNotifier, snapshot, result);
    }

    private Task CancelMatchmakingAsync(string playerId, string reason,
        CancellationToken cancellationToken)
    {
        return CancelMatchmakingAsync(
            _actors,
            _matchmakingNotifier,
            playerId,
            reason,
            cancellationToken);
    }

    private static async Task CancelMatchmakingAsync(
        ActorAccess actors,
        MatchmakingNotifier matchmakingNotifier,
        string playerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var userId = new UserId(playerId);
        var snapshot = await actors
            .Route<UserActor>(userId)
            .CallAsync(
                static behavior => behavior.GetSnapshotAsync,
                new PlayerSessionSnapshotRequest(),
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.SessionToken) &&
            string.IsNullOrWhiteSpace(snapshot.MatchmakingTicketId))
        {
            return;
        }

        await actors
            .Startup<MatchmakingActor>(new MatchmakingQueueId("default"))
            .CallAsync(
                static behavior => behavior.CancelAsync,
                new MatchmakingCancelRequest
                {
                    UserId = playerId,
                    TicketId = snapshot.MatchmakingTicketId,
                    CancelledAtUtc = DateTime.UtcNow,
                    Reason = reason
                },
                cancellationToken)
            .ConfigureAwait(false);

        await actors
            .Route<UserActor>(userId)
            .CallAsync(
                static behavior => behavior.ClearQueueAsync,
                new PlayerSessionQueueClearRequest
                {
                    UserId = playerId,
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

        matchmakingNotifier.Publish(controlSession, new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Canceled,
            QueueSize = 0,
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(reason) ? "Matchmaking cancelled" : reason
        });
    }

    private Task ReleasePlayerAsync(string playerId, string reason,
        CancellationToken cancellationToken)
    {
        return ReleasePlayerAsync(
            _actors,
            _matchmakingNotifier,
            _localNode,
            _logger,
            playerId,
            reason,
            cancellationToken);
    }

    internal static async Task ReleasePlayerAsync(
        ActorAccess actors,
        MatchmakingNotifier matchmakingNotifier,
        LocalActorNodeIdentity localNode,
        ILogger<PlayerService> logger,
        string playerId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = new UserId(playerId);
            var snapshot = await actors
                .Route<UserActor>(userId)
                .CallAsync(
                    static behavior => behavior.GetSnapshotAsync,
                    new PlayerSessionSnapshotRequest(),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(snapshot.MatchmakingTicketId))
            {
                await CancelMatchmakingAsync(actors, matchmakingNotifier, playerId, reason,
                    cancellationToken).ConfigureAwait(false);
                snapshot = await actors
                    .Route<UserActor>(userId)
                    .CallAsync(
                        static behavior => behavior.GetSnapshotAsync,
                        new PlayerSessionSnapshotRequest(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var roomId = snapshot.CurrentRoomId;
            if (!string.IsNullOrWhiteSpace(roomId))
            {
                try
                {
                    await LeaveAssignedRoomAsync(actors, localNode, snapshot, reason, cancellationToken)
                        .ConfigureAwait(false);
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

                await actors
                    .Route<UserActor>(userId)
                    .CallAsync(
                        static behavior => behavior.ClearRoomAsync,
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

            await actors
                .Route<UserActor>(userId)
                .CallAsync(
                    static behavior => behavior.MarkDisconnectedAsync,
                    new PlayerSessionDisconnectRequest
                    {
                        UserId = playerId,
                        ConnectionId = snapshot.ConnectionId,
                        DisconnectedAtUtc = DateTime.UtcNow,
                        Reason = reason
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            await actors
                .Route<UserActor>(userId)
                .CallAsync(
                    static behavior => behavior.SetOnlineAsync,
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
        ActorAccess actors,
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
            return actors.Local<RoomActor>(roomId).CallAsync(static behavior => behavior.LeaveAsync, request, cancellationToken);
        }

        return actors
            .Route<RoomActor>(roomId)
            .CallAsync(static behavior => behavior.LeaveAsync, request, cancellationToken);
    }

    private static void PublishQueued(MatchmakingNotifier matchmakingNotifier, PlayerSessionSnapshot snapshot,
        MatchmakingEnqueueResult result)
    {
        if (!TryCreateControlSession(snapshot, out var controlSession))
        {
            return;
        }

        matchmakingNotifier.Publish(controlSession, new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Queued,
            QueuePosition = result.QueuePosition,
            QueueSize = Math.Max(result.QueuePosition, 1),
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(result.Message) ? "Queued for matchmaking" : result.Message
        });
    }

    internal static async Task PublishMatchedAsync(
        ActorAccess actors,
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
            var userRef = actors.Route<UserActor>(new UserId(player.UserId));
            var snapshot = await userRef
                .CallAsync(
                    static behavior => behavior.GetSnapshotAsync,
                    new PlayerSessionSnapshotRequest(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(snapshot.SessionToken))
            {
                continue;
            }

            await userRef
                .CallAsync(
                    static behavior => behavior.ClearQueueAsync,
                    new PlayerSessionQueueClearRequest
                    {
                        UserId = player.UserId,
                        TicketId = snapshot.MatchmakingTicketId,
                        ClearedAtUtc = DateTime.UtcNow,
                        Reason = "Matched"
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            await userRef
                .CallAsync(
                    static behavior => behavior.AssignRoomAsync,
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

            matchmakingNotifier.Publish(controlSession, new MatchmakingStatusUpdate
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
            });
        }
    }

    private static bool TryCreateControlSession(PlayerSessionSnapshot snapshot, out GameSessionKey controlSession)
    {
        if (string.IsNullOrWhiteSpace(snapshot.UserId) ||
            string.IsNullOrWhiteSpace(snapshot.ControlSessionId))
        {
            controlSession = default;
            return false;
        }

        controlSession = new GameSessionKey(
            snapshot.UserId,
            snapshot.ControlSessionId);
        return true;
    }
}
