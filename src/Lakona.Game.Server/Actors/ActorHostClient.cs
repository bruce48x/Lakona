using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;

namespace Lakona.Game.Server.Actors;

internal sealed class ActorHostClient(
    IClusterClientFactory clients,
    IClusterMembership membership) : IActorHostClient
{
    public async ValueTask<ActorHostCommandReply> CreateAsync(
        ActorHostCreateCommand command,
        CancellationToken cancellationToken = default) =>
        await SendCreateAsync(command, cancellationToken).ConfigureAwait(false);

    public async ValueTask<ActorHostCommandReply> DestroyAsync(
        ActorHostDestroyCommand command,
        CancellationToken cancellationToken = default) =>
        await SendDestroyAsync(command, cancellationToken).ConfigureAwait(false);

    private async ValueTask<ActorHostCommandReply> SendCreateAsync(
        ActorHostCreateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var client = await GetClientAsync(command.Target, cancellationToken).ConfigureAwait(false);
        var reply = await client.CallAsync(
                ActorLifecycleProtocol.Create,
                ActorLifecycleWireRequest.From(command),
                cancellationToken)
            .ConfigureAwait(false);
        return ToDomain(reply);
    }

    private async ValueTask<ActorHostCommandReply> SendDestroyAsync(
        ActorHostDestroyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var client = await GetClientAsync(command.Target, cancellationToken).ConfigureAwait(false);
        var reply = await client.CallAsync(
                ActorLifecycleProtocol.Destroy,
                ActorLifecycleWireRequest.From(command),
                cancellationToken)
            .ConfigureAwait(false);
        return ToDomain(reply);
    }

    private async ValueTask<IRpcClient> GetClientAsync(
        ActorLifecycleTarget target,
        CancellationToken cancellationToken)
    {
        var snapshot = membership.Current;
        var member = snapshot.Members.SingleOrDefault(value =>
            value.State == ClusterMemberState.Ready && value.Reference == target.Owner)
            ?? throw new ActorDirectoryUnavailableException(
                $"Actor host '{target.Owner.Node.Value}' is not one exact Ready member.");
        var location = new RouteLocation(new RouteKey("actor-lifecycle"), member.Reference, snapshot.View, member.ClusterEndpoint);
        return await clients.GetClientAsync(location, cancellationToken).ConfigureAwait(false);
    }

    private static ActorHostCommandReply ToDomain(ActorLifecycleReply reply) => new(
        reply.Succeeded,
        string.IsNullOrWhiteSpace(reply.OwnerNode)
            ? (NodeId?)null
            : new NodeId(reply.OwnerNode!),
        reply.Message);
}
