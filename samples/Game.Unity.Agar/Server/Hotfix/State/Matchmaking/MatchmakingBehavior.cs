using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Matchmaking;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Matchmaking;
using Agar.Sample.State.Rooms;
using Agar.Sample.State.Sessions;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Server.Hotfix.Services;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Sessions;

namespace Server.Hotfix.State.Matchmaking;

[HotfixBehaviorOf(typeof(MatchmakingActor))]
public static class MatchmakingBehavior
{
    public static async ValueTask<MatchmakingEnqueueResult> EnqueueAsync(this MatchmakingActor self, MatchmakingEnqueueRequest request)
    {
        var userId = NormalizeUserId(request.UserId);
        var enqueuedAtUtc = NormalizeUtc(request.EnqueuedAtUtc);
        EnsureState(self);

        var sessionSnapshot = await GetSessionSnapshotAsync(self, userId).ConfigureAwait(false);
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
            QueueId = self.State.QueueId,
            Priority = request.Priority
        };

        self.State.PendingTickets.Add(ticket);
        SortQueue(self);
        self.State.LastUpdatedAtUtc = enqueuedAtUtc;

        await MarkQueuedAsync(self, new PlayerSessionQueueRequest
        {
            UserId = userId,
            QueueId = self.State.QueueId,
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

    public static async ValueTask<MatchmakingCancelResult> CancelAsync(this MatchmakingActor self, MatchmakingCancelRequest request)
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
        self.State.LastUpdatedAtUtc = cancelledAtUtc;

        await ClearQueueAsync(self, new PlayerSessionQueueClearRequest
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

    public static ValueTask<MatchmakingStatusSnapshot> GetStatusAsync(this MatchmakingActor self)
    {
        EnsureState(self);
        return new ValueTask<MatchmakingStatusSnapshot>(new MatchmakingStatusSnapshot
        {
            QueueId = self.State.QueueId,
            DefaultRoomSize = self.State.DefaultRoomSize,
            QueuedCount = self.State.PendingTickets.Count,
            LastMatchId = self.State.LastMatchId,
            LastRoomId = self.State.LastRoomId,
            LastUpdatedAtUtc = self.State.LastUpdatedAtUtc,
            PendingTickets = self.State.PendingTickets.Select(CloneTicket).ToList()
        });
    }

    public static async ValueTask TickAsync(this MatchmakingActor self, HotfixActorTick tick)
    {
        EnsureState(self);
        if (self.State.PendingTickets.Count == 0)
        {
            return;
        }

        var observedAtUtc = tick.ObservedAtUtc == default ? DateTime.UtcNow : tick.ObservedAtUtc;
        var roomSize = MatchmakingQueuePolicy.NormalizeRoomSize(self.State.DefaultRoomSize);
        if (MatchmakingQueuePolicy.GetMatchBatchSize(self.State.PendingTickets, roomSize, observedAtUtc, allowExpiredPartialBatch: true) <= 0)
        {
            return;
        }

        var assignments = await TryMatchAsync(self, observedAtUtc, allowExpiredPartialBatch: true).ConfigureAwait(false);
        foreach (var assignment in assignments.Values.DistinctBy(static assignment => assignment.RoomId))
        {
            var localActors = self.Context.Runtime;
            await PlayerService.PublishMatchedAsync(
                AgarServiceDependencies.From(self.Context.Services, localActors),
                assignment).ConfigureAwait(false);
        }
    }

    public static async ValueTask<Dictionary<string, RoomAssignment>> TryMatchAsync(
        this MatchmakingActor self,
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
                var runtimeGateway = await ResolveRuntimeGatewayAsync(self, batch).ConfigureAwait(false);
                if (runtimeGateway is null)
                {
                    RestoreBatch(self, batch);
                    break;
                }

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

                var createResult = await CreateRoomAsync(self, new RoomCreateRequest
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
                    RestoreBatch(self, batch);
                    break;
                }

                await StartRoomAsync(self, new RoomStartRequest
                {
                    RoomId = roomId,
                    StartedByUserId = batch[0].UserId,
                    StartedAtUtc = nowUtc
                }).ConfigureAwait(false);

                foreach (var playerAssignment in playerAssignments)
                {
                    await AssignRoomAsync(self, playerAssignment).ConfigureAwait(false);
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

                self.State.LastMatchId = matchId;
                self.State.LastRoomId = roomId;
                self.State.LastUpdatedAtUtc = nowUtc;
            }
            catch
            {
                RestoreBatch(self, batch);
                throw;
            }
        }

        return assignments;
    }

    public static async ValueTask<GatewayEndpointDescriptor?> ResolveRuntimeGatewayAsync(
        this MatchmakingActor self,
        IReadOnlyList<MatchmakingQueueTicket> batch)
    {
        _ = batch;
        return await self.RuntimeGateways.ResolveAsync().ConfigureAwait(false);
    }

    private static ValueTask<PlayerSessionSnapshot> GetSessionSnapshotAsync(MatchmakingActor self, string userId)
    {
        var localActors = self.Context.Runtime;
        return localActors.AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
            SessionId(userId),
            (actor, _) => actor.GetSnapshotAsync());
    }

    private static ValueTask<PlayerSessionSnapshot> MarkQueuedAsync(MatchmakingActor self, PlayerSessionQueueRequest request)
    {
        var localActors = self.Context.Runtime;
        return localActors.AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
            SessionId(request.UserId),
            (actor, _) => actor.MarkQueuedAsync(request));
    }

    private static ValueTask<PlayerSessionSnapshot> ClearQueueAsync(MatchmakingActor self, PlayerSessionQueueClearRequest request)
    {
        var localActors = self.Context.Runtime;
        return localActors.AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
            SessionId(request.UserId),
            (actor, _) => actor.ClearQueueAsync(request));
    }

    private static ValueTask<PlayerSessionSnapshot> AssignRoomAsync(MatchmakingActor self, PlayerRoomAssignment request)
    {
        var localActors = self.Context.Runtime;
        return localActors.AskAsync<PlayerSessionActor, PlayerSessionSnapshot>(
            SessionId(request.UserId),
            (actor, _) => actor.AssignRoomAsync(request));
    }

    private static ValueTask<RoomSettlementResult> CreateRoomAsync(MatchmakingActor self, RoomCreateRequest request)
    {
        var localActors = self.Context.Runtime;
        return localActors.AskAsync<RoomActor, RoomSettlementResult>(
            RoomId(request.RoomId),
            (actor, _) => actor.CreateAsync(request));
    }

    private static ValueTask<RoomSettlementResult> StartRoomAsync(MatchmakingActor self, RoomStartRequest request)
    {
        var localActors = self.Context.Runtime;
        return localActors.AskAsync<RoomActor, RoomSettlementResult>(
            RoomId(request.RoomId),
            (actor, _) => actor.StartAsync(request));
    }

    private static ActorId SessionId(string userId) => ActorId.From($"session:{userId}");

    private static ActorId RoomId(string roomId) => ActorId.From(roomId);

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
            if (string.IsNullOrWhiteSpace(self.State.QueueId))
            {
                self.State.QueueId = self.Context.Id.Value;
            }

            if (self.State.DefaultRoomSize <= 0)
            {
                self.State.DefaultRoomSize = MatchmakingActor.DefaultRoomSize;
            }

            return;
        }

        self.State = new MatchmakingState
        {
            QueueId = self.Context.Id.Value,
            DefaultRoomSize = MatchmakingActor.DefaultRoomSize,
            LastUpdatedAtUtc = DateTime.UtcNow
        };
        self.RecordExists = true;
    }

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
