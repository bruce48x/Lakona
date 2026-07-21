using Server.App.State.Contracts;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Timers;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Matchmaking;
using Server.App.State.Rooms;
using Server.App.State.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Server.Hotfix.Services;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Server.Hotfix.Timers;

namespace Server.Hotfix.State.Matchmaking;

[HotfixBehaviorOf(typeof(MatchmakingActor))]
public sealed partial class MatchmakingBehavior
{
    private readonly ActorAccess _actors;
    private readonly MatchmakingNotifier _notifier;

    public MatchmakingBehavior(
        ActorAccess actors,
        MatchmakingNotifier notifier)
    {
        _actors = actors;
        _notifier = notifier;
    }

    [ActorStart]
    public ValueTask StartAsync(MatchmakingActor self, ActorStartCall call)
    {
        return StartTimerAsync(self, new MatchmakingTimerStartRequest(), call.CancellationToken);
    }

    [ActorStop]
    public ValueTask StopAsync(MatchmakingActor self, ActorStopCall call)
    {
        return StopTimerAsync(self, new MatchmakingTimerStopRequest(), call.CleanupCancellationToken);
    }

    public async ValueTask<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingActor self, MatchmakingEnqueueRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var enqueuedAtUtc = NormalizeUtc(request.EnqueuedAtUtc);
        EnsureState(self);

        var sessionSnapshot = await GetSessionSnapshotAsync(userId).ConfigureAwait(false);
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

        var existingTicket = self.State.PendingTickets.FirstOrDefault(ticket => string.Equals(ticket.UserId, userId, StringComparison.Ordinal));
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
                    QueuePosition = GetQueuePosition(self, existingTicket.TicketId),
                    Message = "Player is already queued.",
                    UpdatedAtUtc = enqueuedAtUtc
                };
            }

            self.State.PendingTickets.Remove(existingTicket);
        }

        var ticket = new MatchmakingQueueTicket
        {
            TicketId = Guid.NewGuid().ToString("N"),
            UserId = userId,
            SessionToken = request.SessionToken,
            EnqueuedAtUtc = enqueuedAtUtc,
            QueueId = GetQueueId(self),
            Priority = request.Priority,
            ControlSessionId = request.ControlSessionId
        };

        self.State.PendingTickets.Add(ticket);
        SortQueue(self);
        await MarkQueuedAsync(new PlayerSessionQueueRequest
        {
            UserId = userId,
            TicketId = ticket.TicketId,
            QueuedAtUtc = enqueuedAtUtc
        }).ConfigureAwait(false);

        var assignments = await TryMatchAsync(self, enqueuedAtUtc, allowExpiredPartialBatch: false).ConfigureAwait(false);
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
            QueuePosition = GetQueuePosition(self, ticket.TicketId),
            Message = "Queued for matchmaking.",
            UpdatedAtUtc = enqueuedAtUtc
        };
    }

    public async ValueTask<MatchmakingCancelResult> CancelAsync(MatchmakingActor self, MatchmakingCancelRequest request, CancellationToken cancellationToken = default)
    {
        var userId = NormalizeUserId(request.UserId);
        var cancelledAtUtc = NormalizeUtc(request.CancelledAtUtc);
        EnsureState(self);

        var index = FindTicketIndex(self, request.TicketId, userId);
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

        var ticket = self.State.PendingTickets[index];
        self.State.PendingTickets.RemoveAt(index);
        await ClearQueueAsync(new PlayerSessionQueueClearRequest
        {
            UserId = userId,
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

    public ValueTask<MatchmakingStatusSnapshot> GetStatusAsync(MatchmakingActor self, MatchmakingStatusRequest request, CancellationToken cancellationToken = default)
    {
        EnsureState(self);
        return new ValueTask<MatchmakingStatusSnapshot>(new MatchmakingStatusSnapshot
        {
            QueueId = GetQueueId(self),
            DefaultRoomSize = self.State.DefaultRoomSize,
            QueuedCount = self.State.PendingTickets.Count,
            PendingTickets = self.State.PendingTickets.Select(CloneTicket).ToList()
        });
    }

    public async ValueTask RunTickAsync(MatchmakingActor self, MatchmakingTickRequest request, CancellationToken cancellationToken = default)
    {
        EnsureState(self);
        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        var assignments = await TryMatchAsync(self, observedAtUtc, allowExpiredPartialBatch: true).ConfigureAwait(false);
        await PublishMatchedAsync(assignments.Values).ConfigureAwait(false);
    }

    public ValueTask StartTimerAsync(MatchmakingActor self, MatchmakingTimerStartRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        return EnsureMatchmakingTimerAsync(self, cancellationToken);
    }

    public ValueTask StopTimerAsync(MatchmakingActor self, MatchmakingTimerStopRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return DestroyMatchmakingTimerAsync(self);
    }

    internal static async ValueTask EnsureMatchmakingTimerAsync(MatchmakingActor self, CancellationToken cancellationToken)
    {
        EnsureState(self);
        if (self.MatchmakingTimerId.IsValid)
        {
            return;
        }

        self.MatchmakingTimerId = await LakonaTimer
            .CreatePeriodicTimerAsync(
                static (MatchmakingTimerCallbacks callbacks) => callbacks.TickAsync,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                new MatchmakingTimerArgs { OwnerActorId = self.Context.Id.Value },
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask DestroyMatchmakingTimerAsync(MatchmakingActor self)
    {
        var timerId = self.MatchmakingTimerId;
        self.MatchmakingTimerId = default;
        if (!timerId.IsValid)
        {
            return;
        }

        await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<Dictionary<string, RoomAssignment>> TryMatchAsync(
        MatchmakingActor self,
        DateTime nowUtc,
        bool allowExpiredPartialBatch)
    {
        var assignments = new Dictionary<string, RoomAssignment>(StringComparer.Ordinal);
        var roomSize = MatchmakingQueuePolicy.NormalizeRoomSize(self.State.DefaultRoomSize);

        while (TryTakeMatchBatch(self, roomSize, nowUtc, allowExpiredPartialBatch, out var batch))
        {
            try
            {
                var roomId = $"room-{Guid.NewGuid():N}";
                var matchId = $"match-{Guid.NewGuid():N}";

                var playerAssignments = batch.Select((ticket, seatIndex) => new PlayerRoomAssignment
                {
                    UserId = ticket.UserId,
                    RoomId = roomId,
                    MatchId = matchId,
                    SeatIndex = seatIndex,
                    SessionToken = ticket.SessionToken,
                    ConnectionId = "",
                    ControlSessionId = ticket.ControlSessionId,
                    AssignedAtUtc = nowUtc
                }).ToList();

                var createResult = await AllocateRoomAsync(new RoomCreateRequest
                {
                    RoomId = roomId,
                    MatchId = matchId,
                    CreatedByUserId = batch[0].UserId,
                    CreatedAtUtc = nowUtc,
                    MaxPlayers = roomSize,
                    Players = playerAssignments.Select(CloneAssignment).ToList()
                }).ConfigureAwait(false);

                if (!createResult.Succeeded)
                {
                    RestoreBatch(self, batch);
                    break;
                }

                var runtimeGateway = createResult.Snapshot.RuntimeGateway;
                if (string.IsNullOrWhiteSpace(runtimeGateway.InstanceId)
                    || string.IsNullOrWhiteSpace(runtimeGateway.Transport)
                    || string.IsNullOrWhiteSpace(runtimeGateway.Host)
                    || runtimeGateway.Port <= 0)
                {
                    RestoreBatch(self, batch);
                    break;
                }

                foreach (var playerAssignment in playerAssignments)
                {
                    playerAssignment.RuntimeGateway = CloneGateway(runtimeGateway);
                    await AssignRoomAsync(playerAssignment).ConfigureAwait(false);
                }

                var roomAssignment = new RoomAssignment
                {
                    RoomId = roomId,
                    MatchId = matchId,
                    AssignedAtUtc = nowUtc,
                    MaxPlayers = roomSize,
                    Players = playerAssignments.Select(CloneAssignment).ToList(),
                    RuntimeGateway = CloneGateway(runtimeGateway)
                };

                foreach (var playerAssignment in playerAssignments)
                {
                    assignments[playerAssignment.UserId] = roomAssignment;
                }

            }
            catch
            {
                RestoreBatch(self, batch);
                throw;
            }
        }

        return assignments;
    }

    private async Task PublishMatchedAsync(IEnumerable<RoomAssignment> assignments)
    {
        foreach (var assignment in assignments
            .Where(static assignment => !string.IsNullOrWhiteSpace(assignment.RoomId))
            .GroupBy(static assignment => assignment.RoomId, StringComparer.Ordinal)
            .Select(static group => group.First()))
        {
            await PlayerService.PublishMatchedAsync(_actors, _notifier, assignment).ConfigureAwait(false);
        }
    }

    private ValueTask<PlayerSessionSnapshot> GetSessionSnapshotAsync(string userId)
    {
        return _actors.Route<UserActor>(new UserId(userId)).CallAsync(
            static behavior => behavior.GetSnapshotAsync,
            new PlayerSessionSnapshotRequest(),
            CancellationToken.None);
    }

    private ValueTask<PlayerSessionSnapshot> MarkQueuedAsync(PlayerSessionQueueRequest request)
    {
        return _actors.Route<UserActor>(new UserId(request.UserId)).CallAsync(
            static behavior => behavior.MarkQueuedAsync,
            request,
            CancellationToken.None);
    }

    private ValueTask<PlayerSessionSnapshot> ClearQueueAsync(PlayerSessionQueueClearRequest request)
    {
        return _actors.Route<UserActor>(new UserId(request.UserId)).CallAsync(
            static behavior => behavior.ClearQueueAsync,
            request,
            CancellationToken.None);
    }

    private ValueTask<PlayerSessionSnapshot> AssignRoomAsync(PlayerRoomAssignment request)
    {
        return _actors.Route<UserActor>(new UserId(request.UserId)).CallAsync(
            static behavior => behavior.AssignRoomAsync,
            request,
            CancellationToken.None);
    }

    private async ValueTask<RoomSettlementResult> AllocateRoomAsync(RoomCreateRequest request)
    {
        var roomId = new RoomId(request.RoomId);
        try
        {
            await _actors.Place<RoomActor>(roomId).CreateAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ActorPlacementException ex)
        {
            return new RoomSettlementResult
            {
                RoomId = request.RoomId,
                Succeeded = false,
                Message = ex.Message
            };
        }

        var create = await _actors.Route<RoomActor>(roomId).CallAsync(
            static behavior => behavior.CreateAsync,
            request,
            CancellationToken.None).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            return create;
        }

        var runtimeGateway = create.Snapshot.RuntimeGateway;
        if (string.IsNullOrWhiteSpace(runtimeGateway.InstanceId)
            || string.IsNullOrWhiteSpace(runtimeGateway.Transport)
            || string.IsNullOrWhiteSpace(runtimeGateway.Host)
            || runtimeGateway.Port <= 0)
        {
            return new RoomSettlementResult
            {
                RoomId = request.RoomId,
                Succeeded = false,
                Message = "The Room owner does not advertise a battle runtime endpoint.",
                UpdatedAtUtc = request.CreatedAtUtc,
                Snapshot = create.Snapshot
            };
        }

        var firstPlayer = request.Players[0];
        var start = await _actors.Route<RoomActor>(roomId).CallAsync(
            static behavior => behavior.StartAsync,
            new RoomStartRequest
            {
                RoomId = request.RoomId,
                StartedByUserId = firstPlayer.UserId,
                StartedAtUtc = request.CreatedAtUtc
            },
            CancellationToken.None).ConfigureAwait(false);
        if (!start.Succeeded)
        {
            return start;
        }

        return start;
    }

    private static uint ComputeStableHash(string value)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            var hash = offsetBasis;
            foreach (var ch in value)
            {
                hash ^= ch;
                hash *= prime;
            }

            return hash;
        }
    }

    private static void RestoreBatch(MatchmakingActor self, List<MatchmakingQueueTicket> batch)
    {
        self.State.PendingTickets.InsertRange(0, batch);
        SortQueue(self);
    }

    private static bool TryTakeMatchBatch(
        MatchmakingActor self,
        int roomSize,
        DateTime nowUtc,
        bool allowExpiredPartialBatch,
        out List<MatchmakingQueueTicket> batch)
    {
        batch = [];
        if (self.State.PendingTickets.Count == 0)
        {
            return false;
        }

        var batchSize = MatchmakingQueuePolicy.GetMatchBatchSize(self.State.PendingTickets, roomSize, nowUtc, allowExpiredPartialBatch);
        if (batchSize <= 0)
        {
            return false;
        }

        batch = self.State.PendingTickets.Take(batchSize).ToList();
        self.State.PendingTickets.RemoveRange(0, batchSize);
        return true;
    }

    private static void EnsureState(MatchmakingActor self)
    {
        if (self.RecordExists)
        {
            if (self.State.DefaultRoomSize <= 0)
            {
                self.State.DefaultRoomSize = MatchmakingActor.DefaultRoomSize;
            }

            return;
        }

        self.State = new MatchmakingState
        {
            DefaultRoomSize = MatchmakingActor.DefaultRoomSize
        };
        self.RecordExists = true;
    }

    private static string GetQueueId(MatchmakingActor self) => self.Context.Id.Value;

    private static int GetQueuePosition(MatchmakingActor self, string ticketId)
    {
        var index = self.State.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
        return index < 0 ? -1 : index + 1;
    }

    private static int FindTicketIndex(MatchmakingActor self, string ticketId, string userId)
    {
        if (!string.IsNullOrWhiteSpace(ticketId))
        {
            var byTicket = self.State.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
            if (byTicket >= 0)
            {
                return byTicket;
            }
        }

        return self.State.PendingTickets.FindIndex(ticket => string.Equals(ticket.UserId, userId, StringComparison.Ordinal));
    }

    private static void SortQueue(MatchmakingActor self)
    {
        self.State.PendingTickets = self.State.PendingTickets
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
            Priority = ticket.Priority,
            ControlSessionId = ticket.ControlSessionId,
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
            ControlSessionId = assignment.ControlSessionId,
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
            MaxPlayers = MatchmakingActor.DefaultRoomSize,
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
                    ControlSessionId = sessionSnapshot.ControlSessionId,
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
