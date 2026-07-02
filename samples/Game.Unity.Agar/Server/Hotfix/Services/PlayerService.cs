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
using Microsoft.Extensions.Logging;
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
    private readonly MatchmakingNotifier _matchmakingNotifier;
    private readonly RoomActors _rooms;
    private readonly UserActors _users;

    public PlayerService(
        UserActors users,
        RoomActors rooms,
        MatchmakingActors matchmaking,
        LeaderboardActors leaderboards,
        MatchmakingNotifier matchmakingNotifier,
        LocalActorNodeIdentity localNode,
        ILogger<PlayerService> logger)
    {
        _users = users;
        _rooms = rooms;
        _matchmaking = matchmaking;
        _leaderboards = leaderboards;
        _matchmakingNotifier = matchmakingNotifier;
        _localNode = localNode;
        _logger = logger;
    }

    public async ValueTask<LeaderboardReply> GetLeaderboardAsync(HotfixServiceCall<LeaderboardRequest> call)
    {
        var req = call.Request;
        _ = await EnsureControlConnectionAsync(call).ConfigureAwait(false);

        var topN = req.TopN <= 0 ? 10 : req.TopN;
        var leaderboardId = new LeaderboardId("current");
        var snapshot = await _leaderboards
            .Get(leaderboardId)
            .GetLeaderboardAsync(new LeaderboardQueryRequest { TopN = topN })
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

    public async ValueTask StartMatchmakingAsync(HotfixServiceCall<MatchmakingRequest> call)
    {
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await EnqueuePlayerAsync(playerId).ConfigureAwait(false);
    }

    public async ValueTask CancelMatchmakingAsync(HotfixServiceCall<CancelMatchmakingRequest> call)
    {
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await CancelMatchmakingAsync(playerId, "Matchmaking cancelled").ConfigureAwait(false);
    }

    public async ValueTask LogoutAsync(HotfixServiceCall<LogoutRequest> call)
    {
        var playerId = await EnsureControlConnectionAsync(call).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return;
        }

        await ReleasePlayerAsync(playerId, "Logout").ConfigureAwait(false);
    }

    private static ValueTask<string?> EnsureControlConnectionAsync<TRequest>(
        HotfixServiceCall<TRequest> call)
    {
        return new ValueTask<string?>(call.CurrentSession?.OwnerKey);
    }

    private Task EnqueuePlayerAsync(string playerId)
    {
        return EnqueuePlayerAsync(
            _users,
            _matchmaking,
            _matchmakingNotifier,
            playerId);
    }

    private static async Task EnqueuePlayerAsync(
        UserActors users,
        MatchmakingActors matchmaking,
        MatchmakingNotifier matchmakingNotifier,
        string playerId)
    {
        var snapshot = await users
            .Get(new UserId(playerId))
            .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.SessionToken))
        {
            throw new InvalidOperationException($"Player '{playerId}' does not have an attached control session.");
        }

        var result = await matchmaking
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
            await users
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
            await users
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
            await PublishMatchedAsync(users, matchmakingNotifier, result.RoomAssignment).ConfigureAwait(false);
            return;
        }

        await PublishQueuedAsync(matchmakingNotifier, snapshot, result).ConfigureAwait(false);
    }

    private Task CancelMatchmakingAsync(string playerId, string reason)
    {
        return CancelMatchmakingAsync(
            _users,
            _matchmaking,
            _matchmakingNotifier,
            playerId,
            reason);
    }

    private static async Task CancelMatchmakingAsync(
        UserActors users,
        MatchmakingActors matchmaking,
        MatchmakingNotifier matchmakingNotifier,
        string playerId,
        string reason)
    {
        var snapshot = await users
            .Get(new UserId(playerId))
            .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(snapshot.SessionToken) &&
            string.IsNullOrWhiteSpace(snapshot.MatchmakingTicketId))
        {
            return;
        }

        await matchmaking
            .Get(new MatchmakingQueueId("default"))
            .CancelAsync(new MatchmakingCancelRequest
            {
                UserId = playerId,
                TicketId = snapshot.MatchmakingTicketId,
                CancelledAtUtc = DateTime.UtcNow,
                Reason = reason
            })
            .ConfigureAwait(false);

        await users
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

    private Task ReleasePlayerAsync(string playerId, string reason)
    {
        return ReleasePlayerAsync(
            _users,
            _rooms,
            _matchmaking,
            _matchmakingNotifier,
            _localNode,
            _logger,
            playerId,
            reason);
    }

    internal static async Task ReleasePlayerAsync(
        UserActors users,
        RoomActors rooms,
        MatchmakingActors matchmaking,
        MatchmakingNotifier matchmakingNotifier,
        LocalActorNodeIdentity localNode,
        ILogger<PlayerService> logger,
        string playerId,
        string reason)
    {
        try
        {
            var snapshot = await users
                .Get(new UserId(playerId))
                .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(snapshot.MatchmakingTicketId))
            {
                await CancelMatchmakingAsync(users, matchmaking, matchmakingNotifier, playerId, reason).ConfigureAwait(false);
                snapshot = await users
                    .Get(new UserId(playerId))
                    .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
                    .ConfigureAwait(false);
            }

            var roomId = snapshot.CurrentRoomId;
            if (!string.IsNullOrWhiteSpace(roomId))
            {
                try
                {
                    await LeaveAssignedRoomAsync(rooms, localNode, snapshot, reason).ConfigureAwait(false);
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

            await users
                .Get(new UserId(playerId))
                .MarkDisconnectedAsync(new PlayerSessionDisconnectRequest
                {
                    UserId = playerId,
                    ConnectionId = snapshot.ConnectionId,
                    DisconnectedAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
                .ConfigureAwait(false);
            await users
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
        RoomActors rooms,
        LocalActorNodeIdentity localNode,
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
        var localNodeId = localNode.NodeId.Value;

        if (string.IsNullOrWhiteSpace(snapshot.RuntimeGateway.InstanceId) ||
            string.Equals(snapshot.RuntimeGateway.InstanceId, localNodeId, StringComparison.Ordinal))
        {
            return rooms.Local(roomId).LeaveAsync(request);
        }

        return rooms
            .Remote(new NodeId(snapshot.RuntimeGateway.InstanceId), roomId)
            .LeaveAsync(request);
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
        RoomAssignment assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment.RoomId))
        {
            return;
        }

        foreach (var player in assignment.Players)
        {
            var user = users.Get(new UserId(player.UserId));
            var snapshot = await user
                .GetSnapshotAsync(new PlayerSessionSnapshotRequest())
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(snapshot.SessionToken))
            {
                continue;
            }

            await user
                .ClearQueueAsync(new PlayerSessionQueueClearRequest
                {
                    UserId = player.UserId,
                    QueueId = string.IsNullOrWhiteSpace(snapshot.QueueId) ? "default" : snapshot.QueueId,
                    TicketId = snapshot.MatchmakingTicketId,
                    ClearedAtUtc = DateTime.UtcNow,
                    Reason = "Matched"
                })
                .ConfigureAwait(false);
            await user
                .AssignRoomAsync(new PlayerRoomAssignment
                {
                    UserId = player.UserId,
                    RoomId = assignment.RoomId,
                    MatchId = assignment.MatchId,
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
