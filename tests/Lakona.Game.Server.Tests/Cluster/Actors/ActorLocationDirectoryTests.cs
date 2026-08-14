using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Rpc.Core;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

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

    [Fact]
    public async Task Replaced_host_incarnation_is_rejected_before_registration()
    {
        var old = Reference("node-a", 1);
        var replacement = Reference("node-a", 2);
        var membership = new MutableMembership(Snapshot(4, old));
        var directory = new ActorLocationDirectory(
            membership,
            new RejectingClientFactory(),
            new LocalActorNodeIdentity(old.Node.Value));
        membership.Current = Snapshot(5, replacement);

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(async () =>
            await directory.AcquireAsync(
                ActorId.From("room/stale"),
                old,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken));

        Assert.Null(await directory.ResolveAsync(ActorId.From("room/stale"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Shard_stabilization_never_exceeds_the_requested_concurrency()
    {
        var local = Reference("node-a", 1);
        var remote = Reference("node-b", 2);
        var snapshot = Snapshot(6, local, remote);
        var ownedShardCount = Enumerable.Range(0, ActorLocationLayout.ShardCount)
            .Count(shard => ActorLocationLayout.GetOwner(shard, snapshot) == local);
        Assert.True(ownedShardCount > 8);
        var client = new BlockingRecoveryClient();
        var directory = new ActorLocationDirectory(
            new MutableMembership(snapshot),
            new FixedClientFactory(client),
            new LocalActorNodeIdentity(local.Node.Value));

        var stabilization = directory.StabilizeAsync(
            snapshot,
            maximumConcurrency: 8,
            TestContext.Current.CancellationToken).AsTask();
        await client.WaitForConcurrencyAsync(8, TestContext.Current.CancellationToken);

        Assert.Equal(8, client.MaximumConcurrency);
        client.Release();
        await stabilization;
        Assert.True(client.CallCount > 8);
    }

    [Fact]
    public async Task Planned_owner_change_recovers_from_surviving_registries()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, nodeA, nodeB);
        var after = Snapshot(5, nodeA, nodeB, nodeC);
        var actor = FindActorMovedTo(nodeC, before, after);
        var activation = ActorActivationId.New();
        var membership = new MutableMembership(before);
        var network = new DirectoryNetworkClientFactory();
        var registryA = new ActorActivationRegistry();
        var directoryA = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeA.Node.Value),
            registryA);
        var directoryB = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeB.Node.Value));
        var directoryC = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeC.Node.Value));
        network.Register(nodeA.Node, directoryA);
        network.Register(nodeB.Node, directoryB);
        network.Register(nodeC.Node, directoryC);

        var acquired = await directoryA.AcquireAsync(
            actor,
            nodeA,
            activation,
            TestContext.Current.CancellationToken);
        Assert.True(acquired.Acquired);
        registryA.Set(acquired.Record);

        membership.Current = after;
        network.ClearCalls();
        var resolved = await directoryC.ResolveAsync(
            actor,
            TestContext.Current.CancellationToken);

        Assert.Equal(nodeA, resolved!.OwnerReference);
        Assert.Equal(activation, resolved.ActivationId);
        Assert.Equal(2, network.MethodIds.Count);
        Assert.All(network.MethodIds, methodId =>
            Assert.Equal(ActorLocationProtocol.RegistrySnapshotMethodId, methodId));
    }

    [Fact]
    public async Task Planned_owner_change_recovers_when_the_old_owner_is_dead()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, nodeA, nodeB);
        var after = Snapshot(5, nodeA, nodeC);
        var actor = FindActorMoved(nodeB, nodeC, before, after);
        var activation = ActorActivationId.New();
        var membership = new MutableMembership(before);
        var network = new DirectoryNetworkClientFactory();
        var registryA = new ActorActivationRegistry();
        var directoryA = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeA.Node.Value),
            registryA);
        var directoryB = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeB.Node.Value));
        var directoryC = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeC.Node.Value));
        network.Register(nodeA.Node, directoryA);
        network.Register(nodeB.Node, directoryB);
        network.Register(nodeC.Node, directoryC);

        var acquired = await directoryA.AcquireAsync(
            actor,
            nodeA,
            activation,
            TestContext.Current.CancellationToken);
        Assert.True(acquired.Acquired);
        registryA.Set(acquired.Record);

        membership.Current = after;
        network.Remove(nodeB.Node);
        network.ClearCalls();
        var resolved = await directoryC.ResolveAsync(
            actor,
            TestContext.Current.CancellationToken);

        Assert.Equal(nodeA, resolved!.OwnerReference);
        Assert.Equal(activation, resolved.ActivationId);
        Assert.NotEmpty(network.MethodIds);
        Assert.All(network.MethodIds, methodId =>
            Assert.Equal(ActorLocationProtocol.RegistrySnapshotMethodId, methodId));
    }

    [Fact]
    public async Task Shard_that_moves_away_and_back_recovers_from_registries()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, nodeA, nodeB);
        var away = Snapshot(5, nodeA, nodeB, nodeC);
        var back = Snapshot(6, nodeA, nodeB);
        var actor = FindActorMovedTo(nodeC, before, away);
        var shard = ActorLocationLayout.GetShard(actor);
        var originalOwner = ActorLocationLayout.GetOwner(shard, before)!;
        Assert.Equal(originalOwner, ActorLocationLayout.GetOwner(shard, back));
        var activation = ActorActivationId.New();
        var membership = new MutableMembership(before);
        var network = new DirectoryNetworkClientFactory();
        var registryA = new ActorActivationRegistry();
        var directoryA = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeA.Node.Value),
            registryA);
        var directoryB = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeB.Node.Value));
        var directoryC = new ActorLocationDirectory(
            membership,
            network,
            new LocalActorNodeIdentity(nodeC.Node.Value));
        network.Register(nodeA.Node, directoryA);
        network.Register(nodeB.Node, directoryB);
        network.Register(nodeC.Node, directoryC);

        var acquired = await directoryA.AcquireAsync(
            actor,
            nodeA,
            activation,
            TestContext.Current.CancellationToken);
        Assert.True(acquired.Acquired);
        registryA.Set(acquired.Record);

        membership.Current = away;
        Assert.NotNull(await directoryC.ResolveAsync(
            actor,
            TestContext.Current.CancellationToken));

        membership.Current = back;
        network.ClearCalls();
        var returnedOwner = originalOwner == nodeA ? directoryA : directoryB;
        var resolved = await returnedOwner.ResolveAsync(
            actor,
            TestContext.Current.CancellationToken);

        Assert.Equal(nodeA, resolved!.OwnerReference);
        Assert.Equal(activation, resolved.ActivationId);
        Assert.NotEmpty(network.MethodIds);
        Assert.All(network.MethodIds, methodId =>
            Assert.Equal(ActorLocationProtocol.RegistrySnapshotMethodId, methodId));
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

    private static ActorId FindActorMovedTo(
        NodeReference expectedOwner,
        ClusterMembershipSnapshot before,
        ClusterMembershipSnapshot after)
    {
        for (var index = 0; index < 10_000; index++)
        {
            var actor = ActorId.From($"room/{index}");
            var shard = ActorLocationLayout.GetShard(actor);
            if (ActorLocationLayout.GetOwner(shard, before) != expectedOwner
                && ActorLocationLayout.GetOwner(shard, after) == expectedOwner)
            {
                return actor;
            }
        }

        throw new InvalidOperationException("No deterministic Actor id moved to the added node.");
    }

    private static ActorId FindActorMoved(
        NodeReference previousOwner,
        NodeReference nextOwner,
        ClusterMembershipSnapshot before,
        ClusterMembershipSnapshot after)
    {
        for (var index = 0; index < 10_000; index++)
        {
            var actor = ActorId.From($"room/dead-owner/{index}");
            var shard = ActorLocationLayout.GetShard(actor);
            if (ActorLocationLayout.GetOwner(shard, before) == previousOwner
                && ActorLocationLayout.GetOwner(shard, after) == nextOwner)
            {
                return actor;
            }
        }

        throw new InvalidOperationException("No deterministic Actor id matched the owner transition.");
    }

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

    private sealed class FixedClientFactory(IRpcClient client) : IClusterClientFactory
    {
        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default) => new(client);
    }

    private sealed class DirectoryNetworkClientFactory : IClusterClientFactory
    {
        private readonly Dictionary<NodeId, ActorLocationDirectory> directories = new();

        public List<int> MethodIds { get; } = [];

        public void Register(NodeId node, ActorLocationDirectory directory)
        {
            directories.Add(node, directory);
        }

        public void Remove(NodeId node)
        {
            directories.Remove(node);
        }

        public void ClearCalls()
        {
            MethodIds.Clear();
        }

        public ValueTask<IRpcClient> GetClientAsync(
            RouteLocation target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<IRpcClient>(
                new DirectoryRpcClient(directories[target.Node], MethodIds));
        }
    }

    private sealed class DirectoryRpcClient(
        ActorLocationDirectory directory,
        List<int> methodIds) : IRpcClient
    {
        public async ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct = default)
        {
            methodIds.Add(method.MethodId);
            if (arg is ActorLocationRequest locationRequest)
            {
                var reply = await directory.HandleAsync(
                    (RpcMethod<ActorLocationRequest, ActorLocationReply>)(object)method,
                    locationRequest,
                    ct).ConfigureAwait(false);
                return (TResult)(object)reply;
            }

            if (method.MethodId == ActorLocationProtocol.RegistrySnapshotMethodId
                && arg is ActorRegistrySnapshotRequest snapshotRequest)
            {
                return (TResult)(object)directory.HandleRegistrySnapshot(snapshotRequest);
            }

            throw new NotSupportedException(
                $"The test network does not support Actor Location method '{method.MethodId}'.");
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class BlockingRecoveryClient : IRpcClient
    {
        private readonly TaskCompletionSource reachedLimit = NewSource();
        private readonly TaskCompletionSource release = NewSource();
        private int concurrency;
        private int callCount;
        private int maximumConcurrency;

        public int CallCount => Volatile.Read(ref callCount);
        public int MaximumConcurrency => Volatile.Read(ref maximumConcurrency);

        public async ValueTask<TResult> CallAsync<TArg, TResult>(
            RpcMethod<TArg, TResult> method,
            TArg? arg,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref callCount);
            var active = Interlocked.Increment(ref concurrency);
            UpdateMaximum(active);
            if (active >= 8) reachedLimit.TrySetResult();
            try
            {
                await release.Task.WaitAsync(ct);
                return (TResult)(object)new ActorRegistrySnapshotReply { RecoveryEligible = true };
            }
            finally
            {
                Interlocked.Decrement(ref concurrency);
            }
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler)
        {
        }

        public Task WaitForConcurrencyAsync(int expected, CancellationToken cancellationToken)
        {
            Assert.Equal(8, expected);
            return reachedLimit.Task.WaitAsync(cancellationToken);
        }

        public void Release() => release.TrySetResult();

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref maximumConcurrency);
                if (current >= value || Interlocked.CompareExchange(ref maximumConcurrency, value, current) == current)
                    return;
            }
        }

        private static TaskCompletionSource NewSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
