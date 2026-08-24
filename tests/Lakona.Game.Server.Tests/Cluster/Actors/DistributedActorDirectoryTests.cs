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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delayed_release_cannot_delete_the_replacement(bool remoteDirectoryOwner)
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var snapshot = remoteDirectoryOwner
            ? Snapshot(4, Active(nodeA), Active(nodeB))
            : Snapshot(4, Active(nodeA));
        var actor = remoteDirectoryOwner
            ? FindActorOwnedBy(nodeB, snapshot)
            : ActorId.From("room/42");
        var network = new DirectoryNetwork();
        var membershipA = new MutableMembership(snapshot);
        var directoryA = Directory(nodeA, membershipA, network, snapshot);
        network.Register(nodeA, directoryA);
        if (remoteDirectoryOwner)
        {
            var membershipB = new MutableMembership(snapshot);
            var directoryB = Directory(nodeB, membershipB, network, snapshot);
            network.Register(nodeB, directoryB);
            await directoryB.EnsureViewAsync(snapshot.View, TestContext.Current.CancellationToken);
        }

        await directoryA.EnsureViewAsync(snapshot.View, TestContext.Current.CancellationToken);
        var previous = ActorActivationId.New();
        var replacement = ActorActivationId.New();
        Assert.True((await directoryA.AcquireAsync(
            actor,
            nodeA,
            previous,
            TestContext.Current.CancellationToken)).Acquired);
        Assert.True(await directoryA.ReleaseAsync(
            actor,
            previous,
            TestContext.Current.CancellationToken));
        Assert.True((await directoryA.AcquireAsync(
            actor,
            nodeA,
            replacement,
            TestContext.Current.CancellationToken)).Acquired);

        Assert.False(await directoryA.ReleaseAsync(
            actor,
            previous,
            TestContext.Current.CancellationToken));
        Assert.Equal(
            replacement,
            (await directoryA.ResolveAsync(actor, TestContext.Current.CancellationToken))!.ActivationId);
    }

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
    public async Task Transient_partition_snapshot_failure_does_not_poison_the_new_owner_range()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var actor = FindMovedActor(nodeB, before, after);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(actor);
        var network = new DirectoryNetwork
        {
            PartitionSnapshotFailuresRemaining = 1,
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = actor
        };
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
    }

    [Fact]
    public async Task Stale_snapshot_view_is_retried_without_losing_activation()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var actor = FindMovedActor(nodeB, before, after);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(actor);
        var network = new DirectoryNetwork
        {
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = actor,
            ReturnStaleEmptyPartitionSnapshotOnce = true
        };
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
    }

    [Fact]
    public async Task Transient_second_snapshot_page_failure_retries_the_whole_range()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var actors = FindMovedActorsInOnePartitionPair(nodeB, before, after, 257);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(actors[0]);
        var network = new DirectoryNetwork
        {
            PartitionSnapshotFailuresRemaining = 1,
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = actors[0],
            PartitionSnapshotTargetOffset = 256
        };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after);
        var directoryB = Directory(nodeB, membershipB, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        await Task.WhenAll(
            directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
        var activations = actors.ToDictionary(actor => actor, _ => ActorActivationId.New());
        foreach (var (actor, activation) in activations)
        {
            Assert.True((await directoryA.AcquireAsync(
                actor,
                nodeA,
                activation,
                TestContext.Current.CancellationToken)).Acquired);
        }

        membershipA.Current = after;
        membershipB.Current = after;
        var resolved = await Task.WhenAll(actors.Select(actor =>
            directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken).AsTask()));

        Assert.All(resolved, Assert.NotNull);
        Assert.Equal(
            activations.Values.OrderBy(static activation => activation.Value),
            resolved.Select(static record => record!.ActivationId)
                .OrderBy(static activation => activation.Value));
    }

    [Fact]
    public async Task Replayed_snapshot_page_is_retried_without_losing_activation()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var actors = FindMovedActorsInOnePartitionPair(nodeB, before, after, 257);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(actors[0]);
        var network = new DirectoryNetwork
        {
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = actors[0],
            ReplayFirstPartitionSnapshotPageAtOffset = 256
        };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after);
        var directoryB = Directory(nodeB, membershipB, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        await Task.WhenAll(
            directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
        var activations = actors.ToDictionary(actor => actor, _ => ActorActivationId.New());
        foreach (var (actor, activation) in activations)
        {
            Assert.True((await directoryA.AcquireAsync(
                actor,
                nodeA,
                activation,
                TestContext.Current.CancellationToken)).Acquired);
        }

        membershipA.Current = after;
        membershipB.Current = after;
        var lastActor = actors.OrderBy(static actor => actor.Value, StringComparer.Ordinal).Last();
        var resolved = await directoryB.ResolveAsync(lastActor, TestContext.Current.CancellationToken);

        Assert.Equal(activations[lastActor], resolved!.ActivationId);
    }

    [Fact]
    public async Task Truncated_non_final_snapshot_page_is_retried_without_losing_activation()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var actors = FindMovedActorsInOnePartitionPair(nodeB, before, after, 257);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(actors[0]);
        var network = new DirectoryNetwork
        {
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = actors[0],
            TruncateFirstPartitionSnapshotPage = true
        };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after);
        var directoryB = Directory(nodeB, membershipB, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        await Task.WhenAll(
            directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
        var activations = actors.ToDictionary(actor => actor, _ => ActorActivationId.New());
        foreach (var (actor, activation) in activations)
        {
            Assert.True((await directoryA.AcquireAsync(
                actor,
                nodeA,
                activation,
                TestContext.Current.CancellationToken)).Acquired);
        }

        membershipA.Current = after;
        membershipB.Current = after;
        var omittedActor = actors.OrderBy(static actor => actor.Value, StringComparer.Ordinal).ElementAt(255);
        var resolved = await directoryB.ResolveAsync(omittedActor, TestContext.Current.CancellationToken);

        Assert.Equal(activations[omittedActor], resolved!.ActivationId);
    }

    [Fact]
    public async Task Unreachable_active_snapshot_source_keeps_the_range_fail_closed()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var actor = FindMovedActor(nodeB, before, after);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(actor);
        var network = new DirectoryNetwork
        {
            PartitionSnapshotFailuresRemaining = int.MaxValue,
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = actor
        };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after);
        var directoryB = Directory(nodeB, membershipB, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        try
        {
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
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                directoryB.ResolveAsync(actor, timeout.Token).AsTask());
        }
        finally
        {
            directoryB.Dispose();
            directoryA.Dispose();
        }
    }

    [Fact]
    public async Task New_membership_view_waits_for_an_inflight_range_transfer_without_losing_activation()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var middle = Snapshot(5, Active(nodeA), Active(nodeB));
        var final = Snapshot(6, Active(nodeA));
        var actor = FindMovedActor(nodeB, before, middle);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(actor);
        var network = new DirectoryNetwork
        {
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = actor,
            PausePartitionSnapshotResponses = true
        };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, middle);
        var directoryB = Directory(nodeB, membershipB, network, middle);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        try
        {
            await Task.WhenAll(
                directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
            var activation = ActorActivationId.New();
            Assert.True((await directoryA.AcquireAsync(
                actor,
                nodeA,
                activation,
                TestContext.Current.CancellationToken)).Acquired);

            membershipA.Current = middle;
            membershipB.Current = middle;
            var resolveDuringTransfer = directoryB.ResolveAsync(
                actor,
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForPartitionSnapshotResponseAsync(TestContext.Current.CancellationToken);

            membershipA.Current = final;
            membershipB.Current = final;
            await Task.WhenAll(
                directoryA.EnsureViewAsync(final.View, TestContext.Current.CancellationToken).AsTask(),
                directoryB.EnsureViewAsync(final.View, TestContext.Current.CancellationToken).AsTask());
            network.ReleasePartitionSnapshotResponses();

            Assert.Equal(activation, (await resolveDuringTransfer)!.ActivationId);
            Assert.Equal(
                activation,
                (await directoryA.ResolveAsync(actor, TestContext.Current.CancellationToken))!.ActivationId);
        }
        finally
        {
            network.ReleasePartitionSnapshotResponses();
            directoryB.Dispose();
            directoryA.Dispose();
        }
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

    [Fact]
    public async Task Stale_activation_snapshot_view_is_retried_without_losing_activation()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(6, Active(nodeA), Active(nodeB));
        var actor = FindMovedActor(nodeB, before, after);
        var activation = ActorActivationId.New();
        var registryA = new ActorActivationRegistry();
        registryA.Set(new ActorDirectoryRecord(actor, nodeA, activation, DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork
        {
            ActivationSnapshotTargetActor = actor,
            ReturnStaleEmptyActivationSnapshotOnce = true
        };
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
        var resolved = await directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken);

        Assert.Equal(activation, resolved!.ActivationId);
    }

    [Fact]
    public async Task Conflicting_surviving_activations_keep_a_recovered_range_unavailable()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, Active(nodeA), Active(nodeB), Joining(nodeC));
        var after = Snapshot(6, Active(nodeA), Active(nodeB), Active(nodeC));
        var actor = FindMovedActor(nodeC, before, after);
        var registryA = new ActorActivationRegistry();
        var registryB = new ActorActivationRegistry();
        registryA.Set(new ActorDirectoryRecord(
            actor,
            nodeA,
            ActorActivationId.New(),
            DateTimeOffset.UtcNow));
        registryB.Set(new ActorDirectoryRecord(
            actor,
            nodeB,
            ActorActivationId.New(),
            DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork();
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var membershipC = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, after, registryA);
        var directoryB = Directory(nodeB, membershipB, network, after, registryB);
        var directoryC = Directory(nodeC, membershipC, network, after);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        network.Register(nodeC, directoryC);
        await Task.WhenAll(
            directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
            directoryC.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());

        membershipA.Current = after;
        membershipB.Current = after;
        membershipC.Current = after;

        await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() =>
            directoryC.ResolveAsync(actor, TestContext.Current.CancellationToken).AsTask());
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

    private static ActorId FindActorOwnedBy(
        NodeReference expectedOwner,
        ClusterMembershipSnapshot snapshot)
    {
        var ring = new ActorDirectoryRing(snapshot);
        for (var index = 0; index < 100_000; index++)
        {
            var actor = ActorId.From($"room/{index}");
            if (ring.GetOwner(actor).Owner == expectedOwner) return actor;
        }

        throw new InvalidOperationException("No Actor id belongs to the expected directory owner.");
    }

    private static IReadOnlyList<ActorId> FindMovedActorsInOnePartitionPair(
        NodeReference expectedOwner,
        ClusterMembershipSnapshot before,
        ClusterMembershipSnapshot after,
        int count)
    {
        var oldRing = new ActorDirectoryRing(before);
        var newRing = new ActorDirectoryRing(after);
        ActorDirectoryPartitionId? source = null;
        ActorDirectoryPartitionId? destination = null;
        var result = new List<ActorId>(count);
        for (var index = 0; index < 1_000_000 && result.Count < count; index++)
        {
            var actor = ActorId.From($"paged-room/{index}");
            var oldOwner = oldRing.GetOwner(actor);
            var newOwner = newRing.GetOwner(actor);
            if (oldOwner.Owner == expectedOwner || newOwner.Owner != expectedOwner) continue;
            if (source is null)
            {
                source = oldOwner;
                destination = newOwner;
            }

            if (oldOwner == source && newOwner == destination) result.Add(actor);
        }

        if (result.Count == count) return result;
        throw new InvalidOperationException("Not enough Actor ids moved through one partition pair.");
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
            if (membership.Current.View.CompareTo(refreshed.View) < 0)
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
        private readonly TaskCompletionSource partitionSnapshotResponseObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releasePartitionSnapshotResponses =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ActorDirectorySnapshotReply? firstPartitionSnapshotPage;
        private int replayFirstPartitionSnapshotPage = 1;
        private int truncateFirstPartitionSnapshotPage = 1;
        private int returnStaleEmptyPartitionSnapshot = 1;
        private int returnStaleEmptyActivationSnapshot = 1;

        public ConcurrentQueue<int> MethodIds { get; } = [];

        public bool FailAcknowledge { get; init; }

        public int PartitionSnapshotFailuresRemaining;

        public int? PartitionSnapshotTargetIndex { get; init; }

        public ActorId? PartitionSnapshotTargetActor { get; init; }

        public int? PartitionSnapshotTargetOffset { get; init; }

        public bool PausePartitionSnapshotResponses { get; init; }

        public int? ReplayFirstPartitionSnapshotPageAtOffset { get; init; }

        public bool TruncateFirstPartitionSnapshotPage { get; init; }

        public bool ReturnStaleEmptyPartitionSnapshotOnce { get; init; }

        public ActorId? ActivationSnapshotTargetActor { get; init; }

        public bool ReturnStaleEmptyActivationSnapshotOnce { get; init; }

        public bool ShouldFailPartitionSnapshot(ActorDirectoryPartitionSnapshotRequest request) =>
            MatchesPartitionSnapshot(request)
            && Interlocked.Decrement(ref PartitionSnapshotFailuresRemaining) >= 0;

        private bool MatchesPartitionSnapshot(ActorDirectoryPartitionSnapshotRequest request) =>
            (PartitionSnapshotTargetIndex is null
                || PartitionSnapshotTargetIndex == request.PartitionIndex)
            && (PartitionSnapshotTargetActor is null
                || Range(request.Range).Contains(PartitionSnapshotTargetActor.Value))
            && (PartitionSnapshotTargetOffset is null
                || PartitionSnapshotTargetOffset == request.Offset);

        public Task WaitForPartitionSnapshotResponseAsync(CancellationToken cancellationToken) =>
            partitionSnapshotResponseObserved.Task.WaitAsync(cancellationToken);

        public void ReleasePartitionSnapshotResponses() =>
            releasePartitionSnapshotResponses.TrySetResult();

        public async ValueTask PausePartitionSnapshotResponseAsync(
            ActorDirectoryPartitionSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            if (!PausePartitionSnapshotResponses || !MatchesPartitionSnapshot(request)) return;
            partitionSnapshotResponseObserved.TrySetResult();
            await releasePartitionSnapshotResponses.Task.WaitAsync(cancellationToken);
        }

        public ActorDirectorySnapshotReply ReplayPartitionSnapshotPageIfRequested(
            ActorDirectoryPartitionSnapshotRequest request,
            ActorDirectorySnapshotReply reply)
        {
            if (!MatchesPartitionSnapshot(request)) return reply;
            if (ReturnStaleEmptyPartitionSnapshotOnce
                && Interlocked.Exchange(ref returnStaleEmptyPartitionSnapshot, 0) == 1)
                return new ActorDirectorySnapshotReply
                {
                    Available = true,
                    View = request.View - 1,
                    Records = [],
                    HasMore = false
                };
            if (request.Offset == 0)
            {
                firstPartitionSnapshotPage = reply;
                if (TruncateFirstPartitionSnapshotPage
                    && reply.HasMore
                    && Interlocked.Exchange(ref truncateFirstPartitionSnapshotPage, 0) == 1)
                    return new ActorDirectorySnapshotReply
                    {
                        Available = reply.Available,
                        View = reply.View,
                        Records = reply.Records.Take(reply.Records.Count - 1).ToArray(),
                        HasMore = reply.HasMore
                    };
                return reply;
            }

            return request.Offset == ReplayFirstPartitionSnapshotPageAtOffset
                && Interlocked.Exchange(ref replayFirstPartitionSnapshotPage, 0) == 1
                ? firstPartitionSnapshotPage ?? reply
                : reply;
        }

        public ActorDirectorySnapshotReply ReturnStaleActivationSnapshotIfRequested(
            ActorDirectoryActivationSnapshotRequest request,
            ActorDirectorySnapshotReply reply)
        {
            if (!ReturnStaleEmptyActivationSnapshotOnce
                || ActivationSnapshotTargetActor is not { } actor
                || !Range(request.Range).Contains(actor)
                || Interlocked.Exchange(ref returnStaleEmptyActivationSnapshot, 0) != 1)
                return reply;
            return new ActorDirectorySnapshotReply
            {
                Available = true,
                View = request.View - 1,
                Records = [],
                HasMore = false
            };
        }

        private static ActorDirectoryRange Range(ActorDirectoryRangeDto value) => value.Kind switch
        {
            0 => ActorDirectoryRange.Empty,
            1 => ActorDirectoryRange.Create(value.Start, value.End),
            2 => ActorDirectoryRange.Full,
            _ => throw new InvalidOperationException("Invalid injected Actor Directory range.")
        };

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
            if (arg is ActorDirectoryPartitionSnapshotRequest snapshotRequest
                && network.ShouldFailPartitionSnapshot(snapshotRequest))
                throw new InvalidOperationException("Injected partition snapshot failure.");
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
            if (arg is ActorDirectoryPartitionSnapshotRequest partitionSnapshotRequest)
            {
                await network.PausePartitionSnapshotResponseAsync(
                    partitionSnapshotRequest,
                    ct);
                reply = network.ReplayPartitionSnapshotPageIfRequested(
                    partitionSnapshotRequest,
                    (ActorDirectorySnapshotReply)reply);
            }
            else if (arg is ActorDirectoryActivationSnapshotRequest activationSnapshotRequest)
            {
                reply = network.ReturnStaleActivationSnapshotIfRequested(
                    activationSnapshotRequest,
                    (ActorDirectorySnapshotReply)reply);
            }
            return (TResult)reply;
        }

        public void RegisterNotificationHandler<TArg>(
            RpcNotificationMethod<TArg> method,
            Func<TArg, ValueTask> handler) => throw new NotSupportedException();
    }
}
