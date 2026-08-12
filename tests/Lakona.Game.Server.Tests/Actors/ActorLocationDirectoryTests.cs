using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ActorLocationDirectoryTests
{
    [Fact]
    public async Task Harmless_membership_progress_keeps_location_available()
    {
        var owner = Reference("node-a", 1);
        var membership = new MutableMembership(Snapshot(4, owner));
        var directory = new ActorLocationDirectory(
            membership,
            new RejectingClientFactory(),
            new LocalActorNodeIdentity(owner.Node.Value));
        var actor = ActorId.From("room/42");
        var activation = ActorActivationId.New();

        var acquired = await directory.AcquireAsync(
            actor,
            owner,
            activation,
            TestContext.Current.CancellationToken);
        membership.Current = Snapshot(5, owner);
        var resolved = await directory.ResolveAsync(actor, TestContext.Current.CancellationToken);

        Assert.True(acquired.Acquired);
        Assert.Equal(owner, resolved!.OwnerReference);
        Assert.Equal(activation, resolved.ActivationId);
    }

    [Fact]
    public async Task Concurrent_registration_has_one_winner()
    {
        var owner = Reference("node-a", 1);
        var directory = new ActorLocationDirectory(
            new MutableMembership(Snapshot(4, owner)),
            new RejectingClientFactory(),
            new LocalActorNodeIdentity(owner.Node.Value));
        var actor = ActorId.From("room/42");
        var first = ActorActivationId.New();
        var second = ActorActivationId.New();

        var results = await Task.WhenAll(
            directory.AcquireAsync(actor, owner, first, TestContext.Current.CancellationToken).AsTask(),
            directory.AcquireAsync(actor, owner, second, TestContext.Current.CancellationToken).AsTask());

        Assert.Single(results, result => result.Acquired);
        Assert.Equal(results[0].Record.ActivationId, results[1].Record.ActivationId);
    }

    private static readonly ClusterIncarnationId Cluster = new(
        Guid.Parse("10000000-0000-0000-0000-000000000000"));

    private static ClusterMembershipSnapshot Snapshot(long view, params NodeReference[] nodes) => new(
        Cluster,
        new MembershipViewId(view),
        nodes.Select(node => new ClusterMember(
            node,
            ClusterMemberState.Ready,
            new NodeEndpoint($"tcp://{node.Node.Value}:21001"),
            isVoter: true)).ToArray());

    private static NodeReference Reference(string node, int incarnation) => new(
        Cluster,
        new NodeId(node),
        new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000")));

    private sealed class MutableMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; set; } = current;
        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(MembershipViewId after, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RejectingClientFactory : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(RouteLocation target, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The one-node test must remain local.");
    }
}
