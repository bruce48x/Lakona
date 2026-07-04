using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Rooms;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Rooms;

namespace Server.Hotfix.Features;

[HotfixFeature(BattleRuntimeRoomAllocation.FeatureName)]
public sealed class BattleRuntimeFeature : HotfixGameFeature
{
    private readonly ActorHosting _actorHosting;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly RoomActors _rooms;
    private readonly ILogger<BattleRuntimeFeature> _logger;

    public BattleRuntimeFeature(
        ActorHosting actorHosting,
        LocalActorNodeIdentity localNode,
        RoomActors rooms,
        ILogger<BattleRuntimeFeature> logger)
    {
        _actorHosting = actorHosting;
        _localNode = localNode;
        _rooms = rooms;
        _logger = logger;
    }

    public static void Configure(HotfixFeatureContext context)
    {
        context.HandleCommand<BattleRuntimeRoomAllocationRequest, BattleRuntimeRoomAllocationReply>(
            nameof(AllocateRoomAsync));
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
        var actorCreated = false;
        try
        {
            await _actorHosting
                .CreateAsync<RoomActor>(actorId, call.CancellationToken)
                .ConfigureAwait(false);
            actorCreated = true;

            var roomId = new RoomId(payload.RoomId);
            var firstPlayer = payload.Players[0];
            var createdAtUtc = firstPlayer.AssignedAtUtc == default ? DateTime.UtcNow : firstPlayer.AssignedAtUtc;
            var create = await _rooms.Local(roomId).CreateAsync(new RoomCreateRequest
            {
                RoomId = payload.RoomId,
                MatchId = firstPlayer.MatchId,
                CreatedByUserId = firstPlayer.UserId,
                CreatedAtUtc = createdAtUtc,
                MaxPlayers = payload.MaxPlayers,
                Players = payload.Players.Select(CloneAssignment).ToList(),
                RuntimeGateway = CloneGateway(firstPlayer.RuntimeGateway)
            }).ConfigureAwait(false);
            if (!create.Succeeded)
            {
                await DestroyCreatedRoomActorAsync(actorId).ConfigureAwait(false);
                actorCreated = false;
                return CreateReply(payload, false, create.Message);
            }

            var start = await _rooms.Local(roomId).StartAsync(new RoomStartRequest
            {
                RoomId = payload.RoomId,
                StartedByUserId = firstPlayer.UserId,
                StartedAtUtc = createdAtUtc
            }).ConfigureAwait(false);
            if (!start.Succeeded)
            {
                await DestroyCreatedRoomActorAsync(actorId).ConfigureAwait(false);
                actorCreated = false;
                return CreateReply(payload, false, start.Message);
            }

            _logger.LogDebug("Allocated battle-runtime room {RoomId} on node {NodeId}.", payload.RoomId, _localNode.NodeId.Value);
            return CreateReply(payload, true, "Room allocated.");
        }
        catch (ActorAlreadyHostedException) when (!actorCreated)
        {
            return CreateReply(payload, false, $"Room actor {payload.RoomId} already exists.");
        }
        catch (ActorHostedElsewhereException) when (!actorCreated)
        {
            return CreateReply(payload, false, $"Room actor {payload.RoomId} is owned by another node.");
        }
        catch
        {
            if (actorCreated)
            {
                await DestroyCreatedRoomActorAsync(actorId).ConfigureAwait(false);
            }

            throw;
        }
    }

    private async ValueTask DestroyCreatedRoomActorAsync(ActorId actorId)
    {
        try
        {
            await _actorHosting.DestroyAsync<RoomActor>(actorId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compensate room actor {RoomId} creation.", actorId.Value);
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
            Message = message
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
