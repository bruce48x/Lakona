using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Rooms;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Rooms;

namespace Server.Hotfix.Features;

[HotfixFeature(BattleRuntimeRoomAllocation.FeatureName)]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    private readonly ActorHosting _actorHosting;
    private readonly IActorDirectory _directory;
    private readonly IActorDirectoryCache _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly RoomActors _rooms;
    private readonly ILogger<BattleRuntimeFeature> _logger;

    public BattleRuntimeFeature(
        ActorHosting actorHosting,
        IActorDirectory directory,
        IActorDirectoryCache directoryCache,
        LocalActorNodeIdentity localNode,
        RoomActors rooms,
        ILogger<BattleRuntimeFeature> logger)
    {
        _actorHosting = actorHosting;
        _directory = directory;
        _directoryCache = directoryCache;
        _localNode = localNode;
        _rooms = rooms;
        _logger = logger;
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.Services.AddLogging();
        context.HandleCommand<BattleRuntimeRoomAllocationRequest, BattleRuntimeRoomAllocationReply>(
            nameof(AllocateRoomAsync));
    }

    public static async ValueTask StartAsync(HotfixFeatureStartCall call)
    {
        var timerId = await LakonaTimer
            .CreatePeriodicTimerAsync<BattleRuntimeTimerCallbacks, BattleRuntimeTimerArgs>(
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(50),
                nameof(BattleRuntimeTimerCallbacks.TickAsync),
                new BattleRuntimeTimerArgs(),
                call.CancellationToken)
            .ConfigureAwait(false);
        call.State.Items[FeatureTimerKeys.BattleRuntimeScanTimerId] = timerId;
    }

    public static async ValueTask StopAsync(HotfixFeatureStopCall call)
    {
        if (call.State.Items.TryGetValue(FeatureTimerKeys.BattleRuntimeScanTimerId, out var value) &&
            value is TimerId timerId &&
            timerId.IsValid)
        {
            await LakonaTimer.DestroyTimerAsync(timerId, CancellationToken.None).ConfigureAwait(false);
        }

        call.State.Items.Remove(FeatureTimerKeys.BattleRuntimeScanTimerId);
    }

    public async ValueTask<BattleRuntimeRoomAllocationReply> AllocateRoomAsync(
        HotfixFeatureCommandCall<BattleRuntimeRoomAllocationRequest> call)
    {
        var payload = call.Request;
        if (string.IsNullOrWhiteSpace(payload.RoomId) || payload.Players.Count == 0)
        {
            return CreateReply(payload, false, "RoomId and Players are required.");
        }

        var actorId = ActorId.From(payload.RoomId);
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, call.CancellationToken)
            .ConfigureAwait(false);
        var registeredHere = registerStatus == ActorDirectoryRegisterStatus.Registered;
        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            _directoryCache.Remove(actorId);
            return CreateReply(payload, false, $"Room actor {payload.RoomId} is owned by another node.");
        }

        try
        {
            await _actorHosting
                .CreateAsync<RoomActor>(actorId, call.CancellationToken)
                .ConfigureAwait(false);

            var roomId = new RoomId(payload.RoomId);
            var create = await _rooms.Local(roomId).CreateAsync(new RoomCreateRequest
            {
                RoomId = payload.RoomId,
                MatchId = payload.MatchId,
                CreatedByUserId = payload.CreatedByUserId,
                CreatedAtUtc = payload.CreatedAtUtc,
                MaxPlayers = payload.MaxPlayers,
                Players = payload.Players.Select(CloneAssignment).ToList(),
                RuntimeGateway = CloneGateway(payload.RuntimeGateway)
            }).ConfigureAwait(false);
            if (!create.Succeeded)
            {
                return CreateReply(payload, false, create.Message);
            }

            var start = await _rooms.Local(roomId).StartAsync(new RoomStartRequest
            {
                RoomId = payload.RoomId,
                StartedByUserId = payload.CreatedByUserId,
                StartedAtUtc = payload.CreatedAtUtc
            }).ConfigureAwait(false);
            if (!start.Succeeded)
            {
                return CreateReply(payload, false, start.Message);
            }

            _directoryCache.Set(actorId, _localNode.NodeId);
            _logger.LogDebug("Allocated battle-runtime room {RoomId} on node {NodeId}.", payload.RoomId, _localNode.NodeId.Value);
            return CreateReply(payload, true, "Room allocated.");
        }
        catch
        {
            if (registeredHere)
            {
                await _directory.UnregisterAsync(actorId, _localNode.NodeId, call.CancellationToken).ConfigureAwait(false);
            }

            _directoryCache.Remove(actorId);
            throw;
        }
    }

    private static BattleRuntimeRoomAllocationReply CreateReply(
        BattleRuntimeRoomAllocationRequest request,
        bool succeeded,
        string message)
    {
        return new BattleRuntimeRoomAllocationReply
        {
            Succeeded = succeeded,
            RoomId = request.RoomId,
            MatchId = request.MatchId,
            Message = message,
            RuntimeGateway = CloneGateway(request.RuntimeGateway)
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
