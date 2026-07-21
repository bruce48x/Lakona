using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Actors;

public sealed class ReplicatedActorActivationDirectoryTests
{
    [Fact]
    public async Task Closed_authority_gate_rejects_new_activation_work()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("20000000-0000-0000-0000-000000000000"));
        var member = CreateMember(cluster, 1);
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            [member]));
        var network = new InProcessClusterNetwork();
        var gateway = new RemoteActorGateway();
        var directory = new ReplicatedActorActivationDirectory(
            membership,
            network,
            network,
            gateway,
            new LocalActorNodeIdentity(member.Reference.Node),
            new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) },
            new ClosedAdmissionGate());
        network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await directory.AcquireAsync(
                ActorId.From("player:fenced"),
                member.Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Adding_a_node_keeps_existing_actor_activations_sticky()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("10000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 4)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members[..3]));
        var network = new InProcessClusterNetwork();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();

        var actorIds = Enumerable.Range(1, 64)
            .Select(index => ActorId.From($"player:{index}"))
            .ToArray();
        foreach (var actorId in actorIds)
        {
            var acquired = await directories[0].AcquireAsync(
                actorId,
                members[0].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken);
            Assert.True(acquired.Acquired);
        }

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members));

        foreach (var actorId in actorIds)
        {
            var resolved = await directories[3].ResolveAsync(
                actorId,
                TestContext.Current.CancellationToken);
            Assert.NotNull(resolved);
            Assert.Equal(members[0].Reference, resolved.OwnerReference);

            var reacquired = await directories[3].AcquireAsync(
                actorId,
                members[3].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken);
            Assert.False(reacquired.Acquired);
            Assert.Equal(members[0].Reference, reacquired.Record.OwnerReference);
        }
    }

    [Fact]
    public async Task Removed_owner_is_superseded_with_a_higher_activation_version()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("30000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 3)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members));
        var network = new InProcessClusterNetwork();
        var directories = members.Select(member =>
        {
            var gateway = new RemoteActorGateway();
            var directory = new ReplicatedActorActivationDirectory(
                membership,
                network,
                network,
                gateway,
                new LocalActorNodeIdentity(member.Reference.Node),
                new RemoteActorOptions { DefaultTimeout = TimeSpan.FromSeconds(2) });
            network.Register(member.Reference.Node, directory, gateway.CreateReplyHandler());
            return directory;
        }).ToArray();
        var actorId = ActorId.From("player:recover");
        var first = await directories[0].AcquireAsync(
            actorId,
            members[2].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members[..2]));

        var replacement = await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.True(replacement.Acquired);
        Assert.Equal(members[0].Reference, replacement.Record.OwnerReference);
        Assert.NotEqual(first.Record.ActivationId, replacement.Record.ActivationId);
        Assert.True(replacement.Record.Version > first.Record.Version);
    }

    private static ClusterMember CreateMember(ClusterIncarnationId cluster, int index)
    {
        return new ClusterMember(
            new NodeReference(
                cluster,
                new NodeId($"data-{index}"),
                new NodeIncarnationId(Guid.Parse($"{index:D8}-0000-0000-0000-000000000000"))),
            ClusterMemberState.Ready,
            new NodeEndpoint($"tcp://127.0.0.1:{22000 + index}"),
            isVoter: true);
    }

    private sealed class MutableMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; private set; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId observedView,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Current);

        public void Publish(ClusterMembershipSnapshot snapshot) => Current = snapshot;
    }

    private sealed class InProcessClusterNetwork : IExactClusterNodeSender, IClusterNodeSender
    {
        private readonly Dictionary<NodeId, Endpoint> endpoints = new();

        public void Register(
            NodeId node,
            IClusterMessageHandler activationHandler,
            IClusterMessageHandler replyHandler) =>
            endpoints.Add(node, new Endpoint(activationHandler, replyHandler));

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeReference target,
            MembershipViewId view,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default) =>
            endpoints[target.Node].ActivationHandler.HandleAsync(message, cancellationToken);

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            long? expectedNodeEpoch,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default) =>
            endpoints[nodeId].ReplyHandler.HandleAsync(message, cancellationToken);

        private sealed record Endpoint(
            IClusterMessageHandler ActivationHandler,
            IClusterMessageHandler ReplyHandler);
    }

    private sealed class ClosedAdmissionGate : IDistributedWorkAdmissionGate
    {
        public bool IsOpen => false;

        public bool TryEnter(out DistributedWorkAdmission admission)
        {
            admission = default;
            return false;
        }

        public void Exit(DistributedWorkAdmission admission) =>
            throw new InvalidOperationException("No work was admitted.");
    }
}
