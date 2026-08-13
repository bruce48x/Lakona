using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;

namespace Lakona.Game.Server.Actors;

public sealed class ActorHostClient(
    IClusterClientFactory clients,
    IClusterMembership membership) : IActorHostClient
{
    public async ValueTask<ActorHostCreateReply> CreateAsync(
        NodeId node,
        ActorHostCreateRequest request,
        CancellationToken cancellationToken = default) =>
        await SendAsync(node, request, ActorLifecycleProtocol.Create, cancellationToken).ConfigureAwait(false);

    public async ValueTask<ActorHostCreateReply> DestroyAsync(
        NodeId node,
        ActorHostCreateRequest request,
        CancellationToken cancellationToken = default)
        => await SendAsync(node, request, ActorLifecycleProtocol.Destroy, cancellationToken).ConfigureAwait(false);

    private async ValueTask<ActorHostCreateReply> SendAsync(
        NodeId node,
        ActorHostCreateRequest request,
        Lakona.Rpc.Core.RpcMethod<ActorLifecycleRequest, ActorLifecycleReply> method,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = membership.Current;
        var member = snapshot.Members.SingleOrDefault(value =>
            value.State == ClusterMemberState.Ready && value.Reference.Node == node)
            ?? throw new ActorDirectoryUnavailableException($"Actor host '{node.Value}' is not one exact Ready member.");
        var location = new RouteLocation(new RouteKey("actor-lifecycle"), member.Reference, snapshot.View, member.ClusterEndpoint);
        var client = await clients.GetClientAsync(location, cancellationToken).ConfigureAwait(false);
        var reply = await client.CallAsync(method, ToWire(request), cancellationToken)
            .ConfigureAwait(false);
        return new ActorHostCreateReply(reply.Succeeded, reply.OwnerNode, reply.Message);
    }

    private static ActorLifecycleRequest ToWire(ActorHostCreateRequest request) => new()
    {
        Actor = request.Actor,
        ActorId = request.ActorId,
        Mode = request.Mode,
        BuildTag = request.BuildTag,
        ClusterIncarnation = Guid.Parse(request.ClusterIncarnation!),
        NodeIncarnation = Guid.Parse(request.NodeIncarnation!),
        ActivationId = Guid.Parse(request.ActivationId!),
        ActivationVersion = request.ActivationVersion
    };
}
