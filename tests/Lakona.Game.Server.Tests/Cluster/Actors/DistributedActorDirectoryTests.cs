using System.Collections.Concurrent;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class DistributedActorDirectoryTests
{
    [Fact]
    public async Task Concurrent_registration_in_one_view_has_one_exact_winner()
    {
        var node = Reference("node-a", 1);
        var membership = new MutableMembership(Snapshot(4, Active(node)));
        var directory = new DistributedActorDirectory(
            membership,
            new RejectingClientFactory(),
            new LocalActorNodeIdentity(node.Node.Value));
        var actor = ActorId.From("room/42");
        var first = ActorActivationId.New();
        var second = ActorActivationId.New();

        var results = await Task.WhenAll(
            directory.AcquireAsync(actor, node, first, TestContext.Current.CancellationToken).AsTask(),
            directory.AcquireAsync(actor, node, second, TestContext.Current.CancellationToken).AsTask());

        Assert.Single(results, result => result.Acquired);
        Assert.Equal(results[0].Record.ActivationId, results[1].Record.ActivationId);
    }

    [Fact]
    public async Task Old_and_new_view_owners_cannot_both_win_registration()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, Active(nodeA), Active(nodeB), Joining(nodeC));
        var after = Snapshot(5, Active(nodeA), Active(nodeB), Active(nodeC));
        var actor = FindMovedActor(nodeC, before, after);
        var network = new DirectoryNetwork();
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var membershipC = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after);
        var directoryB = Directory(nodeB, membershipB, network, after);
        var directoryC = Directory(nodeC, membershipC, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        network.Register(nodeC, directoryC);
        await Task.WhenAll(
            directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryC.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
        membershipC.Current = after;

        var oldOwner = new ActorDirectoryRing(before).GetOwner(actor).Owner;
        var oldDirectory = oldOwner == nodeA ? directoryA : directoryB;
        var results = await Task.WhenAll(
            oldDirectory.AcquireAsync(
                actor,
                nodeA,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken).AsTask(),
            directoryC.AcquireAsync(
                actor,
                nodeB,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Single(results, result => result.Acquired);
        Assert.Equal(results[0].Record.ActivationId, results[1].Record.ActivationId);
        Assert.Equal(
            results[0].Record.ActivationId,
            (await directoryC.ResolveAsync(actor, TestContext.Current.CancellationToken))!.ActivationId);
    }

    [Fact]
    public async Task Consecutive_view_transfers_partition_snapshot_without_registry_recovery()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var actor = FindMovedActor(nodeB, before, after);
        var network = new DirectoryNetwork();
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after);
        var directoryB = Directory(nodeB, membershipB, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        await Task.WhenAll(
            directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
        var activation = ActorActivationId.New();
        Assert.True((await directoryA.AcquireAsync(
            actor,
            nodeA,
            activation,
            TestContext.Current.CancellationToken)).Acquired);

        membershipA.Current = after;
        membershipB.Current = after;
        network.MethodIds.Clear();
        var resolved = await directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken);

        Assert.Equal(activation, resolved!.ActivationId);
        Assert.Contains(ActorDirectoryProtocol.PartitionSnapshot.MethodId, network.MethodIds);
        Assert.DoesNotContain(ActorDirectoryProtocol.ActivationSnapshot.MethodId, network.MethodIds);
    }

    [Fact]
    public async Task Snapshot_acknowledgement_failure_does_not_discard_committed_transfer()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var actor = FindMovedActor(nodeB, before, after);
        var network = new DirectoryNetwork { FailAcknowledge = true };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after);
        var directoryB = Directory(nodeB, membershipB, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        await Task.WhenAll(
            directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
        var activation = ActorActivationId.New();
        Assert.True((await directoryA.AcquireAsync(
            actor,
            nodeA,
            activation,
            TestContext.Current.CancellationToken)).Acquired);

        membershipA.Current = after;
        membershipB.Current = after;
        var resolved = await directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken);

        Assert.Equal(activation, resolved!.ActivationId);
        Assert.Contains(ActorDirectoryProtocol.AcknowledgeSnapshot.MethodId, network.MethodIds);
        Assert.DoesNotContain(ActorDirectoryProtocol.ActivationSnapshot.MethodId, network.MethodIds);
    }

    [Fact]
    public async Task Skipped_view_recovers_from_surviving_activation_registries()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(6, Active(nodeA), Active(nodeB));
        var actor = FindMovedActor(nodeB, before, after);
        var activation = ActorActivationId.New();
        var registryA = new ActorActivationRegistry();
        registryA.Set(new ActorDirectoryRecord(actor, nodeA, activation, DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork();
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after, registryA);
        var directoryB = Directory(nodeB, membershipB, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        await Task.WhenAll(
            directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());

        membershipA.Current = after;
        membershipB.Current = after;
        network.MethodIds.Clear();
        var resolved = await directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken);

        Assert.Equal(activation, resolved!.ActivationId);
        Assert.Contains(ActorDirectoryProtocol.ActivationSnapshot.MethodId, network.MethodIds);
        Assert.DoesNotContain(ActorDirectoryProtocol.PartitionSnapshot.MethodId, network.MethodIds);
    }

    private static DistributedActorDirectory Directory(
        NodeReference local,
        MutableMembership membership,
        DirectoryNetwork network,
        ClusterMembershipSnapshot refreshed,
        ActorActivationRegistry? registry = null) => new(
        membership,
        network,
        new LocalActorNodeIdentity(local.Node.Value),
        registry,
        new RefreshingMembership(membership, refreshed));

    private static ActorId FindMovedActor(
        NodeReference expectedOwner,
        ClusterMembershipSnapshot before,
        ClusterMembershipSnapshot after)
    {
        var oldRing = new ActorDirectoryRing(before);
        var newRing = new ActorDirectoryRing(after);
        for (var index = 0; index < 100_000; index++)
        {
            var actor = ActorId.From($"room/{index}");
            if (oldRing.GetOwner(actor).Owner != expectedOwner
                && newRing.GetOwner(actor).Owner == expectedOwner)
                return actor;
        }

        throw new InvalidOperationException("No Actor id moved to the expected directory owner.");
    }

    private static readonly ClusterIncarnationId Cluster = new(
        Guid.Parse("10000000-0000-0000-0000-000000000000"));

    private static ClusterMembershipSnapshot Snapshot(long view, params ClusterMember[] members) => new(
        Cluster,
        new MembershipViewId(view),
        members);

    private static ClusterMember Active(NodeReference node) => Member(node, ClusterMemberState.Active);

    private static ClusterMember Joining(NodeReference node) => Member(node, ClusterMemberState.Joining);

    private static ClusterMember Member(NodeReference node, ClusterMemberState state) => new(
        node,
        state,
        new NodeEndpoint($"tcp://{node.Node.Value}:21001"));

    private static NodeReference Reference(string node, int incarnation) => new(
        Cluster,
        new NodeId(node),
        new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000")));

    private sealed class MutableMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; set; } = current;

        public async ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default)
        {
            while (Current.View.CompareTo(after) <= 0)
                await Task.Delay(1, cancellationToken);
            return Current;
        }
    }

    private sealed class RefreshingMembership(
        MutableMembership membership,
        ClusterMembershipSnapshot refreshed) : IClusterMembershipRefresher
    {
        public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            membership.Current = refreshed;
            return default;
        }
    }

    private sealed class RejectingClientFactory : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The one-node test must remain local.");
    }

    private sealed class DirectoryNetwork : IClusterClientFactory
    {
        private readonly Dictionary<NodeReference, DistributedActorDirectory> directories = [];

        public ConcurrentQueue<int> MethodIds { get; } = [];

        public bool FailAcknowledge { get; init; }

        public void Register(NodeReference node, DistributedActorDirectory directory) =>
            directories.Add(node, directory);

        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IRpcClient>(new DirectoryClient(directories[target.NodeReference], this));
        }
    }

    private sealed class DirectoryClient(
        DistributedActorDirectory directory,
        DirectoryNetwork network) : IRpcClient
    {
        public async ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct = default)
        {
            network.MethodIds.Enqueue(method.MethodId);
            if (network.FailAcknowledge
                && method.MethodId == ActorDirectoryProtocol.AcknowledgeSnapshot.MethodId)
                throw new InvalidOperationException("Injected snapshot acknowledgement failure.");
            object reply = arg switch
            {
                ActorDirectoryRequest request => await directory.HandleAsync(
                    (RpcMethod<ActorDirectoryRequest, ActorDirectoryReply>)(object)method,
                    request,
                    ct),
                ActorDirectoryPartitionSnapshotRequest request =>
                    await directory.HandlePartitionSnapshotAsync(request, ct),
                ActorDirectoryActivationSnapshotRequest request =>
                    await directory.HandleActivationSnapshotAsync(request, ct),
                ActorDirectorySnapshotAcknowledgeRequest request =>
                    await directory.HandleAcknowledgeAsync(request, ct),
                _ => throw new NotSupportedException(
                    $"Unsupported Actor Directory method '{method.MethodId}'.")
            };
            return (TResult)reply;
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler) => throw new NotSupportedException();
    }
}
