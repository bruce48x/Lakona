using Server.App.Routing;
using Server.App.Matchmaking;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Timers;
using Server.Hotfix.Players;
using Server.Hotfix.Rooms;
using Server.Hotfix.Users;
using Microsoft.Extensions.Logging;

namespace Server.Hotfix.Matchmaking;

[HotfixBehaviorOf(typeof(MatchmakingActor))]
public sealed partial class MatchmakingBehavior
{
    private readonly ActorAccess _actors;
    private readonly MatchmakingNotifier _notifier;
    private readonly ILogger<MatchmakingBehavior> _logger;

    public MatchmakingBehavior(
        ActorAccess actors,
        MatchmakingNotifier notifier,
        ILogger<MatchmakingBehavior> logger)
    {
        _actors = actors;
        _notifier = notifier;
        _logger = logger;
    }

    [ActorStart]
    public ValueTask StartAsync(MatchmakingActor self, ActorStartCall call)
    {
        EnsureMatchmakingTimer(self);
        return default;
    }

    [ActorStop]
    public ValueTask StopAsync(MatchmakingActor self, ActorStopCall call)
    {
        DestroyMatchmakingTimer(self);
        return default;
    }

    public async ValueTask<MatchmakingEnqueueResult> EnqueueAsync(MatchmakingActor self, MatchmakingEnqueueRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var enqueuedAtUtc = NormalizeUtc(request.EnqueuedAtUtc);

        var existingTicket = self.PendingTickets.FirstOrDefault(ticket => string.Equals(ticket.UserId, userId, StringComparison.Ordinal));
        if (existingTicket is not null && existingTicket.SessionToken == request.SessionToken)
        {
            return new MatchmakingEnqueueResult
            {
                UserId = userId,
                TicketId = existingTicket.TicketId,
                Queued = true,
                QueuePosition = GetQueuePosition(self, existingTicket.TicketId),
                Message = "Player is already queued.",
                UpdatedAtUtc = enqueuedAtUtc
            };
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

        var registration = await MarkQueuedAsync(new PlayerSessionQueueRequest
        {
            UserId = userId,
            TicketId = ticket.TicketId,
            ControlSessionId = request.ControlSessionId,
            QueuedAtUtc = enqueuedAtUtc
        }).ConfigureAwait(false);
        if (!registration.Queued)
        {
            return new MatchmakingEnqueueResult { UserId = userId };
        }

        if (existingTicket is not null)
            self.PendingTickets.Remove(existingTicket);
        self.PendingTickets.Add(ticket);
        SortQueue(self);

        return new MatchmakingEnqueueResult
        {
            UserId = userId,
            TicketId = ticket.TicketId,
            Queued = true,
            QueuePosition = GetQueuePosition(self, ticket.TicketId),
            Message = "Queued for matchmaking.",
            UpdatedAtUtc = enqueuedAtUtc
        };
    }

    public async ValueTask<MatchmakingCancelResult> CancelAsync(MatchmakingActor self, MatchmakingCancelRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var cancelledAtUtc = NormalizeUtc(request.CancelledAtUtc);

        var index = self.PendingTickets.FindIndex(ticket => ticket.UserId == userId && ticket.TicketId == request.TicketId);
        if (index < 0)
        {
            if (self.PendingAssignments.Any(room => room.Players.Any(player =>
                player.UserId == userId && player.TicketId == request.TicketId)))
            {
                await ClearQueueAsync(new PlayerSessionQueueClearRequest
                {
                    UserId = userId, TicketId = request.TicketId, ClearedAtUtc = cancelledAtUtc, Reason = request.Reason
                }).ConfigureAwait(false);
            }
            return new MatchmakingCancelResult
            {
                UserId = userId,
                TicketId = request.TicketId,
                Cancelled = false,
                Message = "No queued ticket was found.",
                UpdatedAtUtc = cancelledAtUtc
            };
        }

        var ticket = self.PendingTickets[index];
        self.PendingTickets.RemoveAt(index);
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

    [global::Lakona.Game.Server.Hotfix.Abstractions.ActorTimer]
    private ValueTask OnTimerAsync(MatchmakingActor self, TimerTick tick) =>
        RunTickAsync(self, new MatchmakingTickRequest { ObservedAtUtc = DateTime.UtcNow });

    internal async ValueTask RunTickAsync(
        MatchmakingActor self,
        MatchmakingTickRequest request)
    {
        if (!await RetryPendingRoomCleanupAsync(self).ConfigureAwait(false))
        {
            return;
        }

        var normalizedObservedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        await TryMatchAsync(self, normalizedObservedAtUtc, allowExpiredPartialBatch: true).ConfigureAwait(false);
        await ConfirmAssignmentsAsync(self).ConfigureAwait(false);
    }
}
