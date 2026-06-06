using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State;
using Gateway.Realtime;
using Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace Gateway.Services;

internal sealed class GatewayMatchmakingCoordinator
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _watchers = new(StringComparer.Ordinal);
    private readonly IMatchmakingStateStore _matchmaking;
    private readonly IPlayerSessionStateStore _sessions;
    private readonly IRoomStateStore _rooms;
    private readonly SessionDirectory _sessionDirectory;
    private readonly MatchmakingMonitor _matchmakingMonitor;
    private readonly RoomRuntimeHost _roomRuntimeHost;
    private readonly GatewayNodeIdentity _gatewayNodeIdentity;
    private readonly ReliableMatchmakingPublisher _reliableMatchmakingPublisher;
    private readonly ILogger<GatewayMatchmakingCoordinator> _logger;

    public GatewayMatchmakingCoordinator(
        IMatchmakingStateStore matchmaking,
        IPlayerSessionStateStore sessions,
        IRoomStateStore rooms,
        SessionDirectory sessionDirectory,
        MatchmakingMonitor matchmakingMonitor,
        RoomRuntimeHost roomRuntimeHost,
        GatewayNodeIdentity gatewayNodeIdentity,
        ReliableMatchmakingPublisher reliableMatchmakingPublisher,
        ILogger<GatewayMatchmakingCoordinator> logger)
    {
        _matchmaking = matchmaking;
        _sessions = sessions;
        _rooms = rooms;
        _sessionDirectory = sessionDirectory;
        _matchmakingMonitor = matchmakingMonitor;
        _roomRuntimeHost = roomRuntimeHost;
        _gatewayNodeIdentity = gatewayNodeIdentity;
        _reliableMatchmakingPublisher = reliableMatchmakingPublisher;
        _logger = logger;
    }

    public async Task EnqueueAsync(string playerId)
    {
        var registration = _sessionDirectory.Get(playerId)
            ?? throw new InvalidOperationException($"Player '{playerId}' is not registered.");

        var result = await _matchmaking.EnqueueAsync(new MatchmakingEnqueueRequest
        {
            UserId = playerId,
            SessionToken = registration.SessionToken,
            EnqueuedAtUtc = DateTime.UtcNow
        }).ConfigureAwait(false);

        _sessionDirectory.SetQueueTicket(playerId, string.IsNullOrWhiteSpace(result.TicketId) ? null : result.TicketId);

        if (result.Matched)
        {
            StopWatcher(playerId);
            await PublishMatchedAsync(playerId, result.RoomAssignment).ConfigureAwait(false);
            return;
        }

        await PublishQueuedAsync(playerId, result).ConfigureAwait(false);
        EnsureWatcher(playerId);
    }

    public async Task CancelAsync(string playerId, string reason)
    {
        StopWatcher(playerId);

        var registration = _sessionDirectory.Get(playerId);
        if (registration is null)
        {
            return;
        }

        await _matchmaking.CancelAsync(new MatchmakingCancelRequest
        {
            UserId = playerId,
            TicketId = registration.MatchmakingTicketId ?? string.Empty,
            CancelledAtUtc = DateTime.UtcNow,
            Reason = reason
        }).ConfigureAwait(false);

        _sessionDirectory.SetQueueTicket(playerId, null);
        await _reliableMatchmakingPublisher.PublishAsync(playerId, new MatchmakingStatusUpdate
        {
            State = Shared.Interfaces.MatchmakingState.Canceled,
            QueueSize = 0,
            RoomCapacity = 10,
            RoomId = string.Empty,
            MatchedPlayerCount = 0,
            Message = string.IsNullOrWhiteSpace(reason) ? "Matchmaking cancelled" : reason
        }).ConfigureAwait(false);
    }

    public async Task ReleasePlayerAsync(string playerId, string reason)
    {
        StopWatcher(playerId);

        var registration = _sessionDirectory.Get(playerId);
        if (registration is not null && !string.IsNullOrWhiteSpace(registration.MatchmakingTicketId))
        {
            await _matchmaking.CancelAsync(new MatchmakingCancelRequest
            {
                UserId = playerId,
                TicketId = registration.MatchmakingTicketId,
                CancelledAtUtc = DateTime.UtcNow,
                Reason = reason
            }).ConfigureAwait(false);
            _sessionDirectory.SetQueueTicket(playerId, null);
        }

        var roomId = registration?.RoomId;
        if (!string.IsNullOrWhiteSpace(roomId))
        {
            await _rooms
                .LeaveAsync(new RoomPlayerLeaveRequest
                {
                    UserId = playerId,
                    RoomId = roomId,
                    LeftAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
                .ConfigureAwait(false);
            await _sessions
                .ClearRoomAsync(new PlayerRoomClearRequest
                {
                    UserId = playerId,
                    RoomId = roomId,
                    ClearedAtUtc = DateTime.UtcNow,
                    Reason = reason
                })
                .ConfigureAwait(false);
            _sessionDirectory.ClearRoom(playerId, roomId);
            await _roomRuntimeHost.RemovePlayerAsync(roomId, playerId).ConfigureAwait(false);
        }
        else
        {
            _sessionDirectory.ClearRoom(playerId);
        }
    }

    private Task PublishQueuedAsync(string playerId, MatchmakingEnqueueResult result)
    {
        return _reliableMatchmakingPublisher.PublishAsync(playerId, new MatchmakingStatusUpdate
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

    private async Task PublishMatchedAsync(string playerId, RoomAssignment assignment)
    {
        var room = await _rooms
            .GetSnapshotAsync(assignment.RoomId)
            .ConfigureAwait(false);

        if (_gatewayNodeIdentity.IsRuntimeOwner(room.RuntimeGateway))
        {
            await _roomRuntimeHost.EnsureRoomReadyAsync(room).ConfigureAwait(false);
        }

        foreach (var player in room.Players)
        {
            var registration = _sessionDirectory.Get(player.UserId);
            if (registration is null)
            {
                continue;
            }

            _sessionDirectory.SetQueueTicket(player.UserId, null);
            _sessionDirectory.AssignRoom(player.UserId, room.RoomId, room.MatchId, player.SeatIndex);

            await _reliableMatchmakingPublisher.PublishAsync(player.UserId, new MatchmakingStatusUpdate
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

    private void EnsureWatcher(string playerId)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_watchers.ContainsKey(playerId))
            {
                return;
            }

            cts = new CancellationTokenSource();
            _watchers.Add(playerId, cts);
        }

        _ = WatchForAssignmentAsync(playerId, cts);
    }

    private async Task WatchForAssignmentAsync(string playerId, CancellationTokenSource cts)
    {
        try
        {
            await _matchmakingMonitor.WatchForRoomAssignmentAsync(playerId, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed while watching matchmaking assignment for player {PlayerId}.", playerId);
        }
        finally
        {
            lock (_gate)
            {
                if (_watchers.TryGetValue(playerId, out var current) && ReferenceEquals(current, cts))
                {
                    _watchers.Remove(playerId);
                }
            }

            cts.Dispose();
        }
    }

    private void StopWatcher(string playerId)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!_watchers.TryGetValue(playerId, out cts))
            {
                return;
            }

            _watchers.Remove(playerId);
        }

        cts.Cancel();
        cts.Dispose();
    }

}
