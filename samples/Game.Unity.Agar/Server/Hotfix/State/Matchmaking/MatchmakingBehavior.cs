using Server.App.State.Contracts;
using Server.App.State.Contracts.Matchmaking;
using Server.App.State.Contracts.Rooms;
using Server.App.State.Contracts.Sessions;
using Server.App.State.Contracts.Users;
using Server.App.State.Matchmaking;
using Server.App.State.Rooms;
using Server.App.State.Users;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Server.Hotfix.Services;
using Server.Hotfix.State.Rooms;
using Server.Hotfix.State.Users;
using Server.Hotfix.Timers;

namespace Server.Hotfix.State.Matchmaking;

[HotfixBehaviorOf(typeof(MatchmakingActor))]
public static partial class MatchmakingBehavior
{
    [ActorStart]
    public static ValueTask StartAsync(MatchmakingActor self, ActorStartCall call)
    {
        return self.StartTimerAsync(new MatchmakingTimerStartRequest(), call.CancellationToken);
    }

    [ActorStop]
    public static ValueTask StopAsync(MatchmakingActor self, ActorStopCall call)
    {
        return self.StopTimerAsync(new MatchmakingTimerStopRequest(), call.CleanupCancellationToken);
    }

    public static async ValueTask<MatchmakingEnqueueResult> EnqueueAsync(this MatchmakingActor self, MatchmakingEnqueueRequest request, CancellationToken cancellationToken = default)
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
            QueueId = GetQueueId(self),
            Priority = request.Priority,
            ControlSessionId = request.ControlSessionId,
            ControlSessionGeneration = request.ControlSessionGeneration
        };

        self.State.PendingTickets.Add(ticket);
        SortQueue(self);
        await MarkQueuedAsync(self, new PlayerSessionQueueRequest
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

    public static async ValueTask<MatchmakingCancelResult> CancelAsync(this MatchmakingActor self, MatchmakingCancelRequest request, CancellationToken cancellationToken = default)
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
        await ClearQueueAsync(self, new PlayerSessionQueueClearRequest
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

    public static ValueTask<MatchmakingStatusSnapshot> GetStatusAsync(this MatchmakingActor self, MatchmakingStatusRequest request, CancellationToken cancellationToken = default)
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

    public static async ValueTask RunTickAsync(this MatchmakingActor self, MatchmakingTickRequest request, CancellationToken cancellationToken = default)
    {
        EnsureState(self);
        var observedAtUtc = NormalizeUtc(request.ObservedAtUtc);
        var assignments = await TryMatchAsync(self, observedAtUtc, allowExpiredPartialBatch: true).ConfigureAwait(false);
        await PublishMatchedAsync(self, assignments.Values).ConfigureAwait(false);
    }

    public static ValueTask StartTimerAsync(this MatchmakingActor self, MatchmakingTimerStartRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        return EnsureMatchmakingTimerAsync(self, cancellationToken);
    }

    public static ValueTask StopTimerAsync(this MatchmakingActor self, MatchmakingTimerStopRequest request, CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;
        return DestroyMatchmakingTimerAsync(self);
    }

    internal static async ValueTask EnsureMatchmakingTimerAsync(this MatchmakingActor self, CancellationToken cancellationToken)
    {
        EnsureState(self);
        if (self.MatchmakingTimerId.IsValid)
        {
            return;
        }

        self.MatchmakingTimerId = await LakonaTimer
            .CreatePeriodicTimerAsync<MatchmakingTimerCallbacks, MatchmakingTimerArgs>(
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                nameof(MatchmakingTimerCallbacks.TickAsync),
                new MatchmakingTimerArgs { OwnerActorId = self.Context.Id.Value },
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask DestroyMatchmakingTimerAsync(this MatchmakingActor self)
    {
        var timerId = self.MatchmakingTimerId;
        self.MatchmakingTimerId = default;
        if (!timerId.IsValid)
        {
            return;
        }

        await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None).ConfigureAwait(false);
    }

    private static async ValueTask<Dictionary<string, RoomAssignment>> TryMatchAsync(
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
                    ControlSessionId = ticket.ControlSessionId,
                    ControlSessionGeneration = ticket.ControlSessionGeneration,
                    AssignedAtUtc = nowUtc,
                    RuntimeGateway = CloneGateway(runtimeGateway)
                }).ToList();

                var createResult = await AllocateRoomAsync(self, new RoomCreateRequest
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

                foreach (var playerAssignment in playerAssignments)
                {
                    await AssignRoomAsync(self, playerAssignment).ConfigureAwait(false);
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

    private static IServiceProvider GetCurrentHotfixServices(IServiceProvider services)
    {
        return services.GetService<IHotfixServiceProviderAccessor>()?.Current ?? services;
    }

    private static async Task PublishMatchedAsync(MatchmakingActor self, IEnumerable<RoomAssignment> assignments)
    {
        var services = GetCurrentHotfixServices(self.Context.Services);
        if (services.GetService<MatchmakingNotifier>() is not { } matchmakingNotifier)
        {
            return;
        }

        var actors = services.GetRequiredService<ActorAccess>();
        foreach (var assignment in assignments
            .Where(static assignment => !string.IsNullOrWhiteSpace(assignment.RoomId))
            .GroupBy(static assignment => assignment.RoomId, StringComparer.Ordinal)
            .Select(static group => group.First()))
        {
            await PlayerService.PublishMatchedAsync(actors, matchmakingNotifier, assignment).ConfigureAwait(false);
        }
    }

    private static ValueTask<GatewayEndpointDescriptor?> ResolveRuntimeGatewayAsync(
        MatchmakingActor self,
        IReadOnlyList<MatchmakingQueueTicket> batch)
    {
        _ = batch;
        var local = ResolveLocalKcpEndpoint(self.Context.Services);
        return local is not null
            ? new ValueTask<GatewayEndpointDescriptor?>(local)
            : ResolveRemoteKcpEndpointAsync(self.Context.Services);
    }

    private static ValueTask<PlayerSessionSnapshot> GetSessionSnapshotAsync(MatchmakingActor self, string userId)
    {
        var actors = GetCurrentHotfixServices(self.Context.Services).GetRequiredService<ActorAccess>();
        return actors.Route<UserActor>(new UserId(userId)).CallAsync(
            UserBehavior.GetSnapshotAsync,
            new PlayerSessionSnapshotRequest(),
            CancellationToken.None);
    }

    private static ValueTask<PlayerSessionSnapshot> MarkQueuedAsync(MatchmakingActor self, PlayerSessionQueueRequest request)
    {
        var actors = GetCurrentHotfixServices(self.Context.Services).GetRequiredService<ActorAccess>();
        return actors.Route<UserActor>(new UserId(request.UserId)).CallAsync(
            UserBehavior.MarkQueuedAsync,
            request,
            CancellationToken.None);
    }

    private static ValueTask<PlayerSessionSnapshot> ClearQueueAsync(MatchmakingActor self, PlayerSessionQueueClearRequest request)
    {
        var actors = GetCurrentHotfixServices(self.Context.Services).GetRequiredService<ActorAccess>();
        return actors.Route<UserActor>(new UserId(request.UserId)).CallAsync(
            UserBehavior.ClearQueueAsync,
            request,
            CancellationToken.None);
    }

    private static ValueTask<PlayerSessionSnapshot> AssignRoomAsync(MatchmakingActor self, PlayerRoomAssignment request)
    {
        var actors = GetCurrentHotfixServices(self.Context.Services).GetRequiredService<ActorAccess>();
        return actors.Route<UserActor>(new UserId(request.UserId)).CallAsync(
            UserBehavior.AssignRoomAsync,
            request,
            CancellationToken.None);
    }

    private static async ValueTask<RoomSettlementResult> AllocateRoomAsync(MatchmakingActor self, RoomCreateRequest request)
    {
        var actors = GetCurrentHotfixServices(self.Context.Services).GetRequiredService<ActorAccess>();
        var roomId = new RoomId(request.RoomId);
        try
        {
            await actors.Place<RoomActor>(roomId).CreateAsync(CancellationToken.None).ConfigureAwait(false);
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

        var create = await actors.Route<RoomActor>(roomId).CallAsync(
            RoomBehavior.CreateAsync,
            request,
            CancellationToken.None).ConfigureAwait(false);
        if (!create.Succeeded)
        {
            return create;
        }

        var firstPlayer = request.Players[0];
        var start = await actors.Route<RoomActor>(roomId).CallAsync(
            RoomBehavior.StartAsync,
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

        return new RoomSettlementResult
        {
            RoomId = request.RoomId,
            Succeeded = true,
            Message = "Room allocated.",
            UpdatedAtUtc = request.CreatedAtUtc
        };
    }

    private static GatewayEndpointDescriptor? ResolveLocalKcpEndpoint(IServiceProvider services)
    {
        var runtime = services.GetService<LakonaGameRuntimeOptions>();
        var localNode = services.GetService<LocalActorNodeIdentity>()?.NodeId.Value ?? runtime?.Node.Id;
        if (!CanOwnBattleRuntime(runtime))
        {
            return null;
        }

        var endpoint = runtime?.Endpoints.FirstOrDefault(IsBattleRuntimeEndpoint);
        if (endpoint is null || string.IsNullOrWhiteSpace(localNode))
        {
            return null;
        }

        var uri = new Uri(endpoint.ToAdvertisedEndpoint(), UriKind.Absolute);
        return new GatewayEndpointDescriptor
        {
            InstanceId = localNode,
            Transport = endpoint.Transport,
            Host = uri.Host,
            Port = uri.Port,
            Path = uri.AbsolutePath == "/" ? string.Empty : uri.AbsolutePath
        };
    }

    private static async ValueTask<GatewayEndpointDescriptor?> ResolveRemoteKcpEndpointAsync(IServiceProvider services)
    {
        var directory = services.GetRequiredService<INodeDirectory>();
        var nodes = await directory
            .QueryAsync(
                new NodeDirectoryQuery("local", actorHostName: "room", state: NodeState.Ready),
                DateTimeOffset.UtcNow)
            .ConfigureAwait(false);
        var candidate = nodes
            .Where(static node => node.State == NodeState.Ready)
            .Where(static node => node.Endpoints.ContainsKey("kcp"))
            .OrderBy(static node => node.NodeId.Value, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate is null)
        {
            return null;
        }

        var endpoint = ClusterEndpoint.Parse(candidate.Endpoints["kcp"].Address);
        return new GatewayEndpointDescriptor
        {
            InstanceId = candidate.NodeId.Value,
            Transport = endpoint.Scheme,
            Host = endpoint.Host,
            Port = endpoint.Port,
            Path = endpoint.Path == "/" ? string.Empty : endpoint.Path
        };
    }

    private static bool CanOwnBattleRuntime(LakonaGameRuntimeOptions? runtime)
    {
        return runtime?.ActorHosts is null ||
            runtime.ActorHosts.Contains("room", StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBattleRuntimeEndpoint(LakonaGameEndpointOptions endpoint)
    {
        if (!string.Equals(endpoint.Transport, "kcp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return endpoint.RpcServices.Count == 0 ||
            endpoint.RpcServices.Contains("battle", StringComparer.OrdinalIgnoreCase) ||
            endpoint.RpcServices.Contains("battle-runtime", StringComparer.OrdinalIgnoreCase);
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
            ControlSessionGeneration = ticket.ControlSessionGeneration,
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
            ControlSessionGeneration = assignment.ControlSessionGeneration,
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
                    ControlSessionGeneration = sessionSnapshot.ControlSessionGeneration,
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
