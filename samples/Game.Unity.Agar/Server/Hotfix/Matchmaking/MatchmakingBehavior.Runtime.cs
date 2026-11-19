using Server.App.Routing;
using Microsoft.Extensions.Logging;
using Server.App.Matchmaking;
using Server.App.Rooms;
using Server.App.Sessions;
using Server.App.Users;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Timers;
using Server.Hotfix.Players;
using Server.Hotfix.Rooms;
using Server.Hotfix.Users;

namespace Server.Hotfix.Matchmaking;

public sealed partial class MatchmakingBehavior
{
    internal static void EnsureMatchmakingTimer(MatchmakingActor self)
    {
        if (self.MatchmakingTimerId.IsValid)
        {
            return;
        }

        self.MatchmakingTimerId = self
            .CreatePeriodicTimer(
                static (MatchmakingBehavior behavior) => behavior.OnTimerAsync,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1));
    }

    internal static void DestroyMatchmakingTimer(MatchmakingActor self)
    {
        var timerId = self.MatchmakingTimerId;
        self.MatchmakingTimerId = default;
        if (!timerId.IsValid)
        {
            return;
        }

        self.DestroyTimer(timerId);
    }

    private async ValueTask TryMatchAsync(
        MatchmakingActor self,
        DateTime nowUtc,
        bool allowExpiredPartialBatch)
    {
        while (TryTakeMatchBatch(self, nowUtc, allowExpiredPartialBatch, out var batch))
        {
            try
            {
                var roomId = $"room-{Guid.NewGuid():N}";
                var matchId = $"match-{Guid.NewGuid():N}";

                var playerAssignments = batch.Select((ticket, seatIndex) => new PlayerRoomAssignment
                {
                    UserId = ticket.UserId,
                    TicketId = ticket.TicketId,
                    RoomId = roomId,
                    MatchId = matchId,
                    SeatIndex = seatIndex,
                    SessionToken = ticket.SessionToken,
                    ConnectionId = "",
                    ControlSessionId = ticket.ControlSessionId,
                    AssignedAtUtc = nowUtc
                }).ToList();

                var createResult = await AllocateRoomAsync(self, new RoomCreateRequest
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
                }

                var roomAssignment = new RoomAssignment
                {
                    RoomId = roomId,
                    MatchId = matchId,
                    AssignedAtUtc = nowUtc,
                    Players = playerAssignments,
                    RuntimeGateway = CloneGateway(runtimeGateway)
                };

                self.PendingAssignments.Add(roomAssignment);
            }
            catch
            {
                RestoreBatch(self, batch);
                throw;
            }
        }

    }

    private async Task ConfirmAssignmentsAsync(MatchmakingActor self)
    {
        foreach (var assignment in self.PendingAssignments.ToArray())
        {
            try
            {
                foreach (var player in assignment.Players.ToArray())
                {
                    if (await AcceptSeatAsync(player).ConfigureAwait(false))
                        continue;

                    try
                    {
                        await _actors.Route<RoomActor>(new RoomId(player.RoomId))
                            .CallAsync(static behavior => behavior.LeaveAsync, new RoomPlayerLeaveRequest
                            {
                                RoomId = player.RoomId,
                                UserId = player.UserId,
                                ControlSessionId = player.ControlSessionId,
                                Reason = "Matchmaking ticket expired",
                                LeftAtUtc = DateTime.UtcNow
                            }).ConfigureAwait(false);
                    }
                    catch (ActorNotFoundException)
                    {
                        // The old room is already gone.
                    }
                    assignment.Players.Remove(player);
                }
                PlayerService.PublishMatched(_notifier, assignment);
                self.PendingAssignments.Remove(assignment);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Will retry seat confirmation for room {RoomId}.", assignment.RoomId);
            }
        }
    }

    private ValueTask<PlayerSessionQueueResult> MarkQueuedAsync(PlayerSessionQueueRequest request)
    {
        return _actors.Route<UserActor>(new UserId(request.UserId)).CallAsync(
            static behavior => behavior.MarkQueuedAsync,
            request);
    }

    private ValueTask ClearQueueAsync(PlayerSessionQueueClearRequest request)
    {
        return _actors.Route<UserActor>(new UserId(request.UserId)).CallAsync(
            static behavior => behavior.ClearQueueAsync,
            request);
    }

    private async ValueTask<bool> AcceptSeatAsync(PlayerRoomAssignment request)
    {
        try
        {
            var reply = await _actors.Route<UserActor>(new UserId(request.UserId))
                .CallAsync(static behavior => behavior.AssignRoomAsync, request).ConfigureAwait(false);
            return reply.Accepted;
        }
        catch (ActorNotFoundException)
        {
            return false;
        }
    }

    private async ValueTask<RoomSettlementResult> AllocateRoomAsync(
        MatchmakingActor self,
        RoomCreateRequest request)
    {
        var roomId = new RoomId(request.RoomId);
        var placement = _actors.Place<RoomActor>(roomId);
        var activationCreated = false;
        var roomCreated = false;
        try
        {
            await placement.CreateAsync(CancellationToken.None).ConfigureAwait(false);
            activationCreated = true;
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

        try
        {
            var create = await _actors.Route<RoomActor>(roomId).CallAsync(
                static behavior => behavior.CreateAsync,
                request).ConfigureAwait(false);
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

            roomCreated = true;
            return create;
        }
        finally
        {
            if (activationCreated && !roomCreated)
            {
                try
                {
                    await placement.DestroyAsync(CancellationToken.None).ConfigureAwait(false);
                    self.PendingRoomCleanupId = "";
                }
                catch (Exception exception)
                {
                    self.PendingRoomCleanupId = request.RoomId;
                    _logger.LogError(
                        exception,
                        "Failed to roll back a Room Actor activation after room allocation failed; matchmaking is paused until cleanup succeeds.");
                }
            }
        }
    }

    private async ValueTask<bool> RetryPendingRoomCleanupAsync(MatchmakingActor self)
    {
        if (string.IsNullOrWhiteSpace(self.PendingRoomCleanupId))
        {
            return true;
        }

        try
        {
            await _actors
                .Place<RoomActor>(new RoomId(self.PendingRoomCleanupId))
                .DestroyAsync(CancellationToken.None)
                .ConfigureAwait(false);
            self.PendingRoomCleanupId = "";
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Room Actor rollback cleanup is still unavailable; matchmaking remains paused.");
            return false;
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

    private static string GetQueueId(MatchmakingActor self) => self.Context.Key;

    private static int GetQueuePosition(MatchmakingActor self, string ticketId)
    {
        var index = self.PendingTickets.FindIndex(ticket => string.Equals(ticket.TicketId, ticketId, StringComparison.Ordinal));
        return index < 0 ? -1 : index + 1;
    }

    private static void SortQueue(MatchmakingActor self)
    {
        self.PendingTickets = self.PendingTickets
            .OrderByDescending(ticket => ticket.Priority)
            .ThenBy(ticket => ticket.EnqueuedAtUtc)
            .ThenBy(ticket => ticket.TicketId, StringComparer.Ordinal)
            .ToList();
    }

    private static PlayerRoomAssignment CloneAssignment(PlayerRoomAssignment assignment)
    {
        return new PlayerRoomAssignment
        {
            UserId = assignment.UserId,
            TicketId = assignment.TicketId,
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
