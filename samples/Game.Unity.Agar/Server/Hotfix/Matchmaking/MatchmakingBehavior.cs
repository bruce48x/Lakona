using Server.App.Routing;
using Server.App.Matchmaking;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Server.Hotfix.Players;
using Server.Hotfix.Rooms;
using Server.Hotfix.Users;

namespace Server.Hotfix.Matchmaking;

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

    [ActorIgnore]
    public ValueTask StartTimerAsync(MatchmakingActor self, MatchmakingTimerStartRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        return EnsureMatchmakingTimerAsync(self, cancellationToken);
    }

    [ActorIgnore]
    public ValueTask StopTimerAsync(MatchmakingActor self, MatchmakingTimerStopRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return DestroyMatchmakingTimerAsync(self);
    }
}
