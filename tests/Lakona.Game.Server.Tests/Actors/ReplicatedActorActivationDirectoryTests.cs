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
    public async Task Expanding_a_single_node_cluster_preserves_the_existing_activation()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("40000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 3)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members[..1]));
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
        var actorId = ActorId.From("matchmaking/@startup/data-1");
        var original = await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members));

        var resolved = await directories[1].ResolveAsync(
            actorId,
            TestContext.Current.CancellationToken);
        var reacquired = await directories[2].AcquireAsync(
            actorId,
            members[2].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.NotNull(resolved);
        Assert.Equal(original.Record.OwnerReference, resolved.OwnerReference);
        Assert.Equal(original.Record.ActivationId, resolved.ActivationId);
        Assert.Equal(original.Record.Version, resolved.Version);
        Assert.False(reacquired.Acquired);
        Assert.Equal(original.Record.OwnerReference, reacquired.Record.OwnerReference);
        Assert.Equal(original.Record.ActivationId, reacquired.Record.ActivationId);
        Assert.Equal(original.Record.Version, reacquired.Record.Version);
    }

    [Fact]
    public async Task Repeated_release_and_reacquire_remains_monotonic_across_expansion()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("50000000-0000-0000-0000-000000000000"));
        var members = Enumerable.Range(1, 3)
            .Select(index => CreateMember(cluster, index))
            .ToArray();
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            members[..1]));
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
        var actorId = ActorId.From("player:repeated-lifecycle");
        var first = await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.True(await directories[0].ReleaseAsync(
            actorId,
            first.Record.ActivationId!.Value,
            first.Record.Version,
            TestContext.Current.CancellationToken));
        Assert.Null(await directories[0].ResolveAsync(
            actorId,
            TestContext.Current.CancellationToken));

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members));

        Assert.Null(await directories[1].ResolveAsync(
            actorId,
            TestContext.Current.CancellationToken));
        var second = await directories[1].AcquireAsync(
            actorId,
            members[1].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.True(second.Acquired);
        Assert.NotEqual(first.Record.ActivationId, second.Record.ActivationId);
        Assert.True(second.Record.Version > first.Record.Version);
        Assert.True(await directories[2].ReleaseAsync(
            actorId,
            second.Record.ActivationId!.Value,
            second.Record.Version,
            TestContext.Current.CancellationToken));

        var third = await directories[0].AcquireAsync(
            actorId,
            members[2].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        Assert.True(third.Acquired);
        Assert.NotEqual(second.Record.ActivationId, third.Record.ActivationId);
        Assert.True(third.Record.Version > second.Record.Version);
        foreach (var directory in directories)
        {
            var resolved = await directory.ResolveAsync(
                actorId,
                TestContext.Current.CancellationToken);
            Assert.NotNull(resolved);
            Assert.Equal(third.Record.OwnerReference, resolved.OwnerReference);
            Assert.Equal(third.Record.ActivationId, resolved.ActivationId);
            Assert.Equal(third.Record.Version, resolved.Version);
        }
    }

    [Fact]
    public async Task Resolve_fails_closed_when_a_ready_member_cannot_reconcile_the_record()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("60000000-0000-0000-0000-000000000000"));
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
        var actorId = ActorId.From("player:reconciliation-failure");
        await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);

        for (var blockedIndex = 0; blockedIndex < members.Length; blockedIndex++)
        {
            network.SetAvailable(members[blockedIndex].Reference.Node, available: false);
            for (var callerIndex = 0; callerIndex < directories.Length; callerIndex++)
            {
                if (callerIndex == blockedIndex)
                {
                    continue;
                }

                await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
                    await directories[callerIndex].ResolveAsync(
                        actorId,
                        TestContext.Current.CancellationToken));
            }

            network.SetAvailable(members[blockedIndex].Reference.Node, available: true);
        }
    }

    [Fact]
    public async Task Released_activations_do_not_resurrect_after_replica_set_contraction()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("70000000-0000-0000-0000-000000000000"));
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
        var activations = new List<ActorDirectoryRecord>();
        for (var index = 0; index < 64; index++)
        {
            var acquired = await directories[0].AcquireAsync(
                ActorId.From($"player:released-before-contraction:{index}"),
                members[index % 3].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken);
            activations.Add(acquired.Record);
        }

        membership.Publish(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(2),
            members));
        foreach (var activation in activations)
        {
            Assert.True(await directories[3].ReleaseAsync(
                activation.ActorId,
                activation.ActivationId!.Value,
                activation.Version,
                TestContext.Current.CancellationToken));
        }

        for (var survivor = 0; survivor < members.Length; survivor++)
        {
            membership.Publish(new ClusterMembershipSnapshot(
                cluster,
                new MembershipViewId(3 + survivor),
                [members[survivor]]));
            foreach (var activation in activations)
            {
                Assert.Null(await directories[survivor].ResolveAsync(
                    activation.ActorId,
                    TestContext.Current.CancellationToken));
            }
        }
    }

    [Fact]
    public async Task Concurrent_reacquire_after_release_has_one_winner()
    {
        var cluster = new ClusterIncarnationId(
            Guid.Parse("80000000-0000-0000-0000-000000000000"));
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
        var actorId = ActorId.From("player:concurrent-reacquire");
        var original = await directories[0].AcquireAsync(
            actorId,
            members[0].Reference,
            ActorActivationId.New(),
            TestContext.Current.CancellationToken);
        Assert.True(await directories[1].ReleaseAsync(
            actorId,
            original.Record.ActivationId!.Value,
            original.Record.Version,
            TestContext.Current.CancellationToken));

        var attempts = directories.Select((directory, index) => directory.AcquireAsync(
                actorId,
                members[index].Reference,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken)
            .AsTask()).ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, static result => result.Acquired);
        Assert.All(results, result =>
        {
            Assert.Equal(results[0].Record.OwnerReference, result.Record.OwnerReference);
            Assert.Equal(results[0].Record.ActivationId, result.Record.ActivationId);
            Assert.Equal(results[0].Record.Version, result.Record.Version);
        });
        Assert.True(results[0].Record.Version > original.Record.Version);
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
        private readonly HashSet<NodeId> unavailable = [];

        public void Register(
            NodeId node,
            IClusterMessageHandler activationHandler,
            IClusterMessageHandler replyHandler) =>
            endpoints.Add(node, new Endpoint(activationHandler, replyHandler));

        public void SetAvailable(NodeId node, bool available)
        {
            if (available)
            {
                unavailable.Remove(node);
            }
            else
            {
                unavailable.Add(node);
            }
        }

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeReference target,
            MembershipViewId view,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default) =>
            unavailable.Contains(target.Node)
                ? new ValueTask<ClusterSendStatus>(ClusterSendStatus.NodeUnavailable)
                : endpoints[target.Node].ActivationHandler.HandleAsync(message, cancellationToken);

        public ValueTask<ClusterSendStatus> SendAsync(
            NodeId nodeId,
            long? expectedNodeEpoch,
            RouteKey route,
            ClusterMessage message,
            CancellationToken cancellationToken = default) =>
            unavailable.Contains(nodeId)
                ? new ValueTask<ClusterSendStatus>(ClusterSendStatus.NodeUnavailable)
                : endpoints[nodeId].ReplyHandler.HandleAsync(message, cancellationToken);

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
