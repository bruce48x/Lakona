using Server.App.Routing;
using Server.App.Matchmaking;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Server.Hotfix.Players;
using Server.Hotfix.Rooms;
using Server.Hotfix.Users;

namespace Server.Hotfix.Matchmaking;

public sealed partial class MatchmakingBehavior
{
    internal static async ValueTask EnsureMatchmakingTimerAsync(MatchmakingActor self, CancellationToken cancellationToken)
    {
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

        while (TryTakeMatchBatch(self, nowUtc, allowExpiredPartialBatch, out var batch))
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
        self.PendingTickets.InsertRange(0, batch);
        SortQueue(self);
    }

    private static bool TryTakeMatchBatch(
        MatchmakingActor self,
        DateTime nowUtc,
        bool allowExpiredPartialBatch,
        out List<MatchmakingQueueTicket> batch)
    {
        batch = [];
        if (self.PendingTickets.Count == 0)
        {
            return false;
        }

        var batchSize = MatchmakingQueuePolicy.GetMatchBatchSize(self.PendingTickets, nowUtc, allowExpiredPartialBatch);
        if (batchSize <= 0)
        {
            return false;
        }

        batch = self.PendingTickets.Take(batchSize).ToList();
        self.PendingTickets.RemoveRange(0, batchSize);
        return true;
    }

    private static string GetQueueId(MatchmakingActor self) => self.Context.Id.Value;

    private static int GetQueuePosition(MatchmakingActor self, string ticketId)
    {
        var index = self.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
        return index < 0 ? -1 : index + 1;
    }

    private static int FindTicketIndex(MatchmakingActor self, string ticketId, string userId)
    {
        if (!string.IsNullOrWhiteSpace(ticketId))
        {
            var byTicket = self.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
            if (byTicket >= 0)
            {
                return byTicket;
            }
        }

        return self.PendingTickets.FindIndex(ticket => string.Equals(ticket.UserId, userId, StringComparison.Ordinal));
    }

    private static void SortQueue(MatchmakingActor self)
    {
        self.PendingTickets = self.PendingTickets
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
