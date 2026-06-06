using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Lakona.Game.Server.Actors;

namespace Agar.Sample.State.Matchmaking;

public sealed class MatchmakingActor : Actor
{
    private const int DefaultRoomSize = 10;

    private readonly IPlayerSessionStateStore _sessions;
    private readonly IRoomStateStore _rooms;
    private bool _recordExists;
    private MatchmakingState _state = new();

    public MatchmakingActor(IPlayerSessionStateStore sessions, IRoomStateStore rooms)
    {
        _sessions = sessions;
        _rooms = rooms;
    }

    public async Task<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingEnqueueRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var enqueuedAtUtc = NormalizeUtc(request.EnqueuedAtUtc);
        EnsureState();

        var sessionSnapshot = await _sessions.GetSnapshotAsync(userId).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(sessionSnapshot.CurrentRoomId))
        {
            return new MatchmakingEnqueueResult
            {
                UserId = userId,
                Queued = false,
                Matched = true,
                Message = "Player is already assigned to a room.",
                UpdatedAtUtc = enqueuedAtUtc,
                RoomAssignment = BuildRoomAssignmentFromSession(sessionSnapshot, enqueuedAtUtc)
            };
        }

        var existingTicket = _state.PendingTickets.FirstOrDefault(ticket => string.Equals(ticket.UserId, userId, StringComparison.Ordinal));
        if (existingTicket is not null)
        {
            if (string.Equals(existingTicket.SessionToken, request.SessionToken, StringComparison.Ordinal))
            {
                return new MatchmakingEnqueueResult
                {
                    UserId = userId,
                    TicketId = existingTicket.TicketId,
                    Queued = true,
                    Matched = false,
                    QueuePosition = GetQueuePosition(existingTicket.TicketId),
                    Message = "Player is already queued.",
                    UpdatedAtUtc = enqueuedAtUtc
                };
            }

            _state.PendingTickets.Remove(existingTicket);
        }

        var ticket = new MatchmakingQueueTicket
        {
            TicketId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            SessionToken = request.SessionToken,
            EnqueuedAtUtc = enqueuedAtUtc,
            QueueId = _state.QueueId,
            Priority = request.Priority
        };

        _state.PendingTickets.Add(ticket);
        SortQueue();
        _state.LastUpdatedAtUtc = enqueuedAtUtc;

        await _sessions.MarkQueuedAsync(new PlayerSessionQueueRequest
        {
            UserId = userId,
            QueueId = _state.QueueId,
            TicketId = ticket.TicketId,
            QueuedAtUtc = enqueuedAtUtc
        }).ConfigureAwait(false);

        var assignments = await TryMatchAsync(enqueuedAtUtc, allowExpiredPartialBatch: false).ConfigureAwait(false);
        if (assignments.TryGetValue(userId, out var roomAssignment))
        {
            return new MatchmakingEnqueueResult
            {
                UserId = userId,
                TicketId = ticket.TicketId,
                Queued = false,
                Matched = true,
                Message = "Matched to a room.",
                UpdatedAtUtc = enqueuedAtUtc,
                RoomAssignment = roomAssignment
            };
        }

        return new MatchmakingEnqueueResult
        {
            UserId = userId,
            TicketId = ticket.TicketId,
            Queued = true,
            Matched = false,
            QueuePosition = GetQueuePosition(ticket.TicketId),
            Message = "Queued for matchmaking.",
            UpdatedAtUtc = enqueuedAtUtc
        };
    }

    public async Task<MatchmakingCancelResult> CancelAsync(MatchmakingCancelRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var cancelledAtUtc = NormalizeUtc(request.CancelledAtUtc);
        EnsureState();

        var index = FindTicketIndex(request.TicketId, userId);
        if (index < 0)
        {
            return new MatchmakingCancelResult
            {
                UserId = userId,
                TicketId = request.TicketId,
                Cancelled = false,
                Message = "No queued ticket was found.",
                UpdatedAtUtc = cancelledAtUtc
            };
        }

        var ticket = _state.PendingTickets[index];
        _state.PendingTickets.RemoveAt(index);
        _state.LastUpdatedAtUtc = cancelledAtUtc;

        await _sessions.ClearQueueAsync(new PlayerSessionQueueClearRequest
        {
            UserId = userId,
            QueueId = ticket.QueueId,
            TicketId = ticket.TicketId,
            ClearedAtUtc = cancelledAtUtc,
            Reason = request.Reason
        }).ConfigureAwait(false);

        return new MatchmakingCancelResult
        {
            UserId = userId,
            TicketId = ticket.TicketId,
            Cancelled = true,
            QueuePosition = index + 1,
            Message = "Matchmaking cancelled.",
            UpdatedAtUtc = cancelledAtUtc
        };
    }

    public Task<MatchmakingStatusSnapshot> GetStatusAsync()
    {
        EnsureState();
        return Task.FromResult(new MatchmakingStatusSnapshot
        {
            QueueId = _state.QueueId,
            DefaultRoomSize = _state.DefaultRoomSize,
            QueuedCount = _state.PendingTickets.Count,
            LastMatchId = _state.LastMatchId,
            LastRoomId = _state.LastRoomId,
            LastUpdatedAtUtc = _state.LastUpdatedAtUtc,
            PendingTickets = _state.PendingTickets.Select(CloneTicket).ToList()
        });
    }

    public async Task TickAsync(MatchmakingTickRequest request)
    {
        EnsureState();
        if (_state.PendingTickets.Count == 0)
        {
            return;
        }

        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        var roomSize = MatchmakingQueuePolicy.NormalizeRoomSize(_state.DefaultRoomSize);
        if (MatchmakingQueuePolicy.GetMatchBatchSize(_state.PendingTickets, roomSize, observedAtUtc, allowExpiredPartialBatch: true) <= 0)
        {
            return;
        }

        await TryMatchAsync(observedAtUtc, allowExpiredPartialBatch: true).ConfigureAwait(false);
    }

    private async Task<Dictionary<string, RoomAssignment>> TryMatchAsync(DateTime nowUtc, bool allowExpiredPartialBatch)
    {
        var assignments = new Dictionary<string, RoomAssignment>(StringComparer.Ordinal);
        var roomSize = MatchmakingQueuePolicy.NormalizeRoomSize(_state.DefaultRoomSize);

        while (TryTakeMatchBatch(roomSize, nowUtc, allowExpiredPartialBatch, out var batch))
        {
            try
            {
                var roomId = $"room-{Guid.NewGuid():N}";
                var matchId = $"match-{Guid.NewGuid():N}";
                var runtimeGateway = await ResolveRuntimeGatewayAsync(batch).ConfigureAwait(false);
                var playerAssignments = batch.Select((ticket, seatIndex) => new PlayerRoomAssignment
                {
                    UserId = ticket.UserId,
                    RoomId = roomId,
                    MatchId = matchId,
                    SeatIndex = seatIndex,
                    SessionToken = ticket.SessionToken,
                    ConnectionId = "",
                    AssignedAtUtc = nowUtc,
                    RuntimeGateway = CloneGateway(runtimeGateway)
                }).ToList();

                var createResult = await _rooms.CreateAsync(new RoomCreateRequest
                {
                    RoomId = roomId,
                    MatchId = matchId,
                    CreatedByUserId = batch[0].UserId,
                    CreatedAtUtc = nowUtc,
                    MaxPlayers = roomSize,
                    Players = playerAssignments.Select(CloneAssignment).ToList(),
                    RuntimeGateway = CloneGateway(runtimeGateway)
                }).ConfigureAwait(false);

                if (!createResult.Succeeded)
                {
                    RestoreBatch(batch);
                    break;
                }

                await _rooms.StartAsync(new RoomStartRequest
                {
                    RoomId = roomId,
                    StartedByUserId = batch[0].UserId,
                    StartedAtUtc = nowUtc
                }).ConfigureAwait(false);

                foreach (var playerAssignment in playerAssignments)
                {
                    await _sessions.AssignRoomAsync(playerAssignment).ConfigureAwait(false);
                }

                var roomAssignment = new RoomAssignment
                {
                    RoomId = roomId,
                    MatchId = matchId,
                    AssignedAtUtc = nowUtc,
                    Players = playerAssignments.Select(CloneAssignment).ToList(),
                    RuntimeGateway = CloneGateway(runtimeGateway)
                };

                foreach (var playerAssignment in playerAssignments)
                {
                    assignments[playerAssignment.UserId] = roomAssignment;
                }

                _state.LastMatchId = matchId;
                _state.LastRoomId = roomId;
                _state.LastUpdatedAtUtc = nowUtc;
            }
            catch
            {
                RestoreBatch(batch);
                throw;
            }
        }

        return assignments;
    }

    private void RestoreBatch(List<MatchmakingQueueTicket> batch)
    {
        _state.PendingTickets.InsertRange(0, batch);
        SortQueue();
    }

    private bool TryTakeMatchBatch(int roomSize, DateTime nowUtc, bool allowExpiredPartialBatch, out List<MatchmakingQueueTicket> batch)
    {
        batch = [];
        if (_state.PendingTickets.Count == 0)
        {
            return false;
        }

        var batchSize = MatchmakingQueuePolicy.GetMatchBatchSize(_state.PendingTickets, roomSize, nowUtc, allowExpiredPartialBatch);
        if (batchSize <= 0)
        {
            return false;
        }

        batch = _state.PendingTickets.Take(batchSize).ToList();
        _state.PendingTickets.RemoveRange(0, batchSize);
        return true;
    }

    private void EnsureState()
    {
        if (_recordExists)
        {
            if (string.IsNullOrWhiteSpace(_state.QueueId))
            {
                _state.QueueId = Context.Id.Value;
            }

            if (_state.DefaultRoomSize <= 0)
            {
                _state.DefaultRoomSize = DefaultRoomSize;
            }

            return;
        }

        _state = new MatchmakingState
        {
            QueueId = Context.Id.Value,
            DefaultRoomSize = DefaultRoomSize,
            LastUpdatedAtUtc = DateTime.UtcNow
        };
        _recordExists = true;
    }

    private int GetQueuePosition(string ticketId)
    {
        var index = _state.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
        return index < 0 ? -1 : index + 1;
    }

    private int FindTicketIndex(string ticketId, string userId)
    {
        if (!string.IsNullOrWhiteSpace(ticketId))
        {
            var byTicket = _state.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
            if (byTicket >= 0)
            {
                return byTicket;
            }
        }

        return _state.PendingTickets.FindIndex(ticket => string.Equals(ticket.UserId, userId, StringComparison.Ordinal));
    }

    private void SortQueue()
    {
        _state.PendingTickets = _state.PendingTickets
            .OrderByDescending(ticket => ticket.Priority)
            .ThenBy(ticket => ticket.EnqueuedAtUtc)
            .ThenBy(ticket => ticket.TicketId, StringComparer.Ordinal)
            .ToList();
    }

    private static MatchmakingQueueTicket CloneTicket(MatchmakingQueueTicket ticket)
    {
        return new MatchmakingQueueTicket
        {
            TicketId = ticket.TicketId,
            UserId = ticket.UserId,
            SessionToken = ticket.SessionToken,
            EnqueuedAtUtc = ticket.EnqueuedAtUtc,
            QueueId = ticket.QueueId,
            Priority = ticket.Priority
        };
    }

    private static PlayerRoomAssignment CloneAssignment(PlayerRoomAssignment assignment)
    {
        return new PlayerRoomAssignment
        {
            UserId = assignment.UserId,
            RoomId = assignment.RoomId,
            MatchId = assignment.MatchId,
            SeatIndex = assignment.SeatIndex,
            SessionToken = assignment.SessionToken,
            ConnectionId = assignment.ConnectionId,
            AssignedAtUtc = assignment.AssignedAtUtc,
            RuntimeGateway = CloneGateway(assignment.RuntimeGateway)
        };
    }

    private static RoomAssignment BuildRoomAssignmentFromSession(PlayerSessionSnapshot sessionSnapshot, DateTime assignedAtUtc)
    {
        return new RoomAssignment
        {
            RoomId = sessionSnapshot.CurrentRoomId,
            MatchId = sessionSnapshot.CurrentMatchId,
            AssignedAtUtc = assignedAtUtc,
            RuntimeGateway = CloneGateway(sessionSnapshot.RuntimeGateway),
            Players =
            [
                new PlayerRoomAssignment
                {
                    UserId = sessionSnapshot.UserId,
                    RoomId = sessionSnapshot.CurrentRoomId,
                    MatchId = sessionSnapshot.CurrentMatchId,
                    SeatIndex = sessionSnapshot.SeatIndex,
                    SessionToken = sessionSnapshot.SessionToken,
                    ConnectionId = sessionSnapshot.ConnectionId,
                    AssignedAtUtc = assignedAtUtc,
                    RuntimeGateway = CloneGateway(sessionSnapshot.RuntimeGateway)
                }
            ]
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

    private async Task<GatewayEndpointDescriptor> ResolveRuntimeGatewayAsync(IReadOnlyList<MatchmakingQueueTicket> batch)
    {
        foreach (var ticket in batch)
        {
            var snapshot = await _sessions.GetSnapshotAsync(ticket.UserId).ConfigureAwait(false);
            if (HasValidGateway(snapshot.ControlGateway))
            {
                return CloneGateway(snapshot.ControlGateway);
            }
        }

        return new GatewayEndpointDescriptor();
    }

    private static bool HasValidGateway(GatewayEndpointDescriptor? gateway)
    {
        return gateway is not null
            && !string.IsNullOrWhiteSpace(gateway.InstanceId)
            && !string.IsNullOrWhiteSpace(gateway.Host)
            && gateway.Port > 0;
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
