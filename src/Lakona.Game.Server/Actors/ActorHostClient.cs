using System.Text.Json;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class ActorHostClient(
    RemoteActorGateway gateway,
    IClusterNodeSender nodeSender,
    LocalActorNodeIdentity localNode,
    RemoteActorOptions? options = null) : IActorHostClient
{
    internal const string MessageKind = "_actor_host_create";
    internal static readonly RouteKey Route = new("actor-host:create");

    private readonly RemoteActorOptions _options = options ?? new RemoteActorOptions();

    public async ValueTask<ActorHostCreateReply> CreateAsync(
        NodeId node,
        ActorHostCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = Guid.NewGuid().ToString("N");
        var timeout = _options.DefaultTimeout;
        var pending = gateway.RegisterPendingAsync(correlationId, timeout, cancellationToken);
        ClusterSendStatus status;
        try
        {
            status = await nodeSender.SendAsync(
                node,
                expectedNodeEpoch: null,
                Route,
                new ClusterMessage(
                    Route,
                    MessageKind,
                    JsonSerializer.SerializeToUtf8Bytes(request),
                    DateTimeOffset.UtcNow.Add(timeout),
                    localNode.NodeId,
                    correlationId,
                    orderedBy: request.ActorId),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            gateway.TryCancelPending(correlationId);
            throw;
        }

        if (status != ClusterSendStatus.Accepted)
        {
            gateway.TryCancelPending(correlationId);
            return new ActorHostCreateReply(false, null, $"Actor host create send failed with cluster status: {status}.");
        }

        var payload = await pending.ConfigureAwait(false);
        return JsonSerializer.Deserialize<ActorHostCreateReply>(payload.Span)
            ?? new ActorHostCreateReply(false, null, "Actor host create returned an empty reply.");
    }
}
