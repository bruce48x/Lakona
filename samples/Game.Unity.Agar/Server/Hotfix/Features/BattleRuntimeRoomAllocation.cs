using System.Text.Json;
using Agar.Sample.State.Contracts;
using Agar.Sample.State.Contracts.Rooms;
using Agar.Sample.State.Contracts.Sessions;
using Agar.Sample.State.Rooms;
using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Microsoft.Extensions.Logging;
using Server.Hotfix.State.Rooms;

namespace Server.Hotfix.Features;

internal static class BattleRuntimeRoomAllocation
{
    public const string FeatureName = "battle-runtime";
    public const string AllocateRoomKind = "agar.battle-runtime.allocate-room.v1";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public sealed class BattleRuntimeRoomAllocationRequest
{
    public string RoomId { get; set; } = "";

    public string MatchId { get; set; } = "";

    public string CreatedByUserId { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; }

    public int MaxPlayers { get; set; } = 10;

    public List<PlayerRoomAssignment> Players { get; set; } = new();

    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

public sealed class BattleRuntimeRoomAllocationReply
{
    public bool Succeeded { get; set; }

    public string RoomId { get; set; } = "";

    public string MatchId { get; set; } = "";

    public string Message { get; set; } = "";

    public GatewayEndpointDescriptor RuntimeGateway { get; set; } = new();
}

internal sealed class BattleRuntimeFeatureMessageHandler : IFeatureMessageHandler
{
    private readonly IActorLifecycle _lifecycle;
    private readonly IActorDirectory _directory;
    private readonly IActorDirectoryCache _directoryCache;
    private readonly LocalActorNodeIdentity _localNode;
    private readonly RoomActors _rooms;
    private readonly ILogger<BattleRuntimeFeatureMessageHandler> _logger;

    public BattleRuntimeFeatureMessageHandler(
        IActorLifecycle lifecycle,
        IActorDirectory directory,
        IActorDirectoryCache directoryCache,
        LocalActorNodeIdentity localNode,
        RoomActors rooms,
        ILogger<BattleRuntimeFeatureMessageHandler> logger)
    {
        _lifecycle = lifecycle;
        _directory = directory;
        _directoryCache = directoryCache;
        _localNode = localNode;
        _rooms = rooms;
        _logger = logger;
    }

    public async ValueTask<FeatureMessageReply> HandleAsync(
        FeatureMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                request.Feature.Value,
                BattleRuntimeRoomAllocation.FeatureName,
                StringComparison.OrdinalIgnoreCase))
        {
            return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
        }

        if (!string.Equals(request.Kind, BattleRuntimeRoomAllocation.AllocateRoomKind, StringComparison.Ordinal))
        {
            return new FeatureMessageReply(ClusterSendStatus.FeatureNotFound, ReadOnlyMemory<byte>.Empty);
        }

        BattleRuntimeRoomAllocationRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BattleRuntimeRoomAllocationRequest>(
                request.Payload.Span,
                BattleRuntimeRoomAllocation.JsonOptions);
        }
        catch (JsonException ex)
        {
            return new FeatureMessageReply(ClusterSendStatus.DeserializationFailed, ReadOnlyMemory<byte>.Empty, ex.Message);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.RoomId) || payload.Players.Count == 0)
        {
            return new FeatureMessageReply(
                ClusterSendStatus.Rejected,
                ReadOnlyMemory<byte>.Empty,
                "RoomId and Players are required.");
        }

        var actorId = ActorId.From(payload.RoomId);
        var registerStatus = await _directory
            .RegisterAsync(actorId, _localNode.NodeId, cancellationToken)
            .ConfigureAwait(false);
        var registeredHere = registerStatus == ActorDirectoryRegisterStatus.Registered;
        if (registerStatus == ActorDirectoryRegisterStatus.Conflict)
        {
            _directoryCache.Remove(actorId);
            return new FeatureMessageReply(
                ClusterSendStatus.StaleRoute,
                ReadOnlyMemory<byte>.Empty,
                $"Room actor {payload.RoomId} is owned by another node.");
        }

        try
        {
            var createResult = await _lifecycle
                .CreateLocalAsync<RoomActor>(actorId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!createResult.Succeeded)
            {
                if (registeredHere)
                {
                    await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
                }

                _directoryCache.Remove(actorId);
                return new FeatureMessageReply(
                    ClusterSendStatus.Failed,
                    ReadOnlyMemory<byte>.Empty,
                    createResult.Diagnostic ?? $"Could not create room actor '{payload.RoomId}'.");
            }

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
                return CreateReply(ClusterSendStatus.Rejected, payload, create.Message);
            }

            var start = await _rooms.Local(roomId).StartAsync(new RoomStartRequest
            {
                RoomId = payload.RoomId,
                StartedByUserId = payload.CreatedByUserId,
                StartedAtUtc = payload.CreatedAtUtc
            }).ConfigureAwait(false);
            if (!start.Succeeded)
            {
                return CreateReply(ClusterSendStatus.Rejected, payload, start.Message);
            }

            _directoryCache.Set(actorId, _localNode.NodeId);
            _logger.LogDebug("Allocated battle-runtime room {RoomId} on node {NodeId}.", payload.RoomId, _localNode.NodeId.Value);
            return CreateReply(ClusterSendStatus.Accepted, payload, "Room allocated.");
        }
        catch (Exception ex)
        {
            if (registeredHere)
            {
                await _directory.UnregisterAsync(actorId, _localNode.NodeId, cancellationToken).ConfigureAwait(false);
            }

            _directoryCache.Remove(actorId);
            return new FeatureMessageReply(ClusterSendStatus.Failed, ReadOnlyMemory<byte>.Empty, ex.Message);
        }
    }

    private static FeatureMessageReply CreateReply(
        ClusterSendStatus status,
        BattleRuntimeRoomAllocationRequest request,
        string message)
    {
        var reply = new BattleRuntimeRoomAllocationReply
        {
            Succeeded = status == ClusterSendStatus.Accepted,
            RoomId = request.RoomId,
            MatchId = request.MatchId,
            Message = message,
            RuntimeGateway = CloneGateway(request.RuntimeGateway)
        };

        return new FeatureMessageReply(
            status,
            JsonSerializer.SerializeToUtf8Bytes(reply, BattleRuntimeRoomAllocation.JsonOptions),
            message);
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
