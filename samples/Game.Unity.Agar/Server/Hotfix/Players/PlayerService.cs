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
                new LeaderboardQueryRequest { TopN = topN })
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
        var snapshot = await GetControlSnapshotAsync(call).ConfigureAwait(false);
        if (snapshot is null)
            return;

        var result = await _actors
            .Startup<MatchmakingActor>(new MatchmakingQueueId("default"))
            .CallAsync(
                static behavior => behavior.EnqueueAsync,
                new MatchmakingEnqueueRequest
                {
                    UserId = snapshot.UserId,
                    SessionToken = snapshot.SessionToken,
                    ControlSessionId = snapshot.ControlSessionId,
                    EnqueuedAtUtc = DateTime.UtcNow
                })
            .ConfigureAwait(false);

        if (result.Queued)
            PublishQueued(_matchmakingNotifier, snapshot, result);
    }

    public async ValueTask CancelMatchmakingAsync(PlayerServiceCall<CancelMatchmakingRequest> call)
    {
        var snapshot = await GetControlSnapshotAsync(call).ConfigureAwait(false);
        if (snapshot is not null)
            await CancelMatchmakingAsync(_actors, _matchmakingNotifier, snapshot, "Matchmaking cancelled").ConfigureAwait(false);
    }

    public async ValueTask LogoutAsync(PlayerServiceCall<LogoutRequest> call)
    {
        if (call.CurrentSession is { } session)
        {
            await ReleasePlayerAsync(_actors, _matchmakingNotifier, _localNode, _logger, session.OwnerKey,
                new ControlSessionRequest { SessionId = session.SessionId, ConnectionId = call.ConnectionId },
                "Logout").ConfigureAwait(false);
        }
    }

    private async ValueTask<PlayerSessionSnapshot?> GetControlSnapshotAsync<TRequest>(PlayerServiceCall<TRequest> call)
    {
        if (call.CurrentSession is not { } session)
            return null;

        var snapshot = await _actors.Route<UserActor>(new UserId(session.OwnerKey))
            .CallAsync(static behavior => behavior.GetSnapshotAsync, new PlayerSessionSnapshotRequest())
            .ConfigureAwait(false);
        return snapshot.SessionToken.Length != 0 && snapshot.ControlSessionId == session.SessionId &&
            snapshot.ConnectionId == call.ConnectionId ? snapshot : null;
    }

    private static async Task CancelMatchmakingAsync(
        ActorAccess actors,
        MatchmakingNotifier matchmakingNotifier,
        PlayerSessionSnapshot snapshot,
        string reason)
    {
        if (string.IsNullOrEmpty(snapshot.MatchmakingTicketId))
            return;

        await actors
            .Startup<MatchmakingActor>(new MatchmakingQueueId("default"))
            .CallAsync(
                static behavior => behavior.CancelAsync,
                new MatchmakingCancelRequest
                {
                    UserId = snapshot.UserId,
                    TicketId = snapshot.MatchmakingTicketId,
                    CancelledAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
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

    internal static async Task ReleasePlayerAsync(
        ActorAccess actors,
        MatchmakingNotifier matchmakingNotifier,
        LocalActorNodeIdentity localNode,
        ILogger<PlayerService> logger,
        string playerId,
        ControlSessionRequest request,
        string reason)
    {
        try
        {
            var user = actors.Route<UserActor>(new UserId(playerId));
            var snapshot = await user.CallAsync(static behavior => behavior.BeginControlReleaseAsync, request)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(snapshot.ControlSessionId))
                return;

            await CancelMatchmakingAsync(actors, matchmakingNotifier, snapshot, reason).ConfigureAwait(false);
            var roomId = snapshot.CurrentRoomId;
            if (!string.IsNullOrWhiteSpace(roomId))
            {
                try
                {
                    await LeaveAssignedRoomAsync(actors, localNode, snapshot, reason)
                        .ConfigureAwait(false);
                }
                catch (ActorNotFoundException)
                {
                    logger.LogDebug(
                        "Room {RoomId} no longer exists while releasing player {PlayerId} during {Reason}; the player cannot be settled by that room again.",
                        roomId,
                        playerId,
                        reason);
                }
            }

            await user.CallAsync(static behavior => behavior.CompleteControlReleaseAsync, request)
                .ConfigureAwait(false);
        }
        catch (ActorNotFoundException)
        {
            logger.LogDebug("Player {PlayerId} has no live User Actor during {Reason}; nothing to release.", playerId, reason);
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
        string reason)
    {
        var request = new RoomPlayerLeaveRequest
        {
            UserId = snapshot.UserId,
            RoomId = snapshot.CurrentRoomId,
            ControlSessionId = snapshot.ControlSessionId,
            LeftAtUtc = DateTime.UtcNow,
            Reason = reason
        };
        var roomId = new RoomId(snapshot.CurrentRoomId);
        var localNodeId = localNode.NodeId.Value;

        if (string.IsNullOrWhiteSpace(snapshot.RuntimeGateway.InstanceId) ||
            string.Equals(snapshot.RuntimeGateway.InstanceId, localNodeId, StringComparison.Ordinal))
        {
            return actors.Local<RoomActor>(roomId)
                .CallAsync(static behavior => behavior.LeaveAsync, request);
        }

        return actors
            .Route<RoomActor>(roomId)
            .CallAsync(static behavior => behavior.LeaveAsync, request);
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

    internal static void PublishMatched(
        MatchmakingNotifier matchmakingNotifier,
        RoomAssignment assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment.RoomId))
        {
            return;
        }

        foreach (var player in assignment.Players)
        {
            if (string.IsNullOrWhiteSpace(player.ControlSessionId))
                continue;

            var controlSession = new GameSessionKey(player.UserId, player.ControlSessionId);
            matchmakingNotifier.Publish(controlSession, new MatchmakingStatusUpdate
            {
                State = Shared.Interfaces.MatchmakingState.Matched,
                QueueSize = assignment.Players.Count,
                RoomCapacity = RoomRules.RoomSize,
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
