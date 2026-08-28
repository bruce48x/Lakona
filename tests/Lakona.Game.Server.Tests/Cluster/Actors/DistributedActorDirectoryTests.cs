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
    public async Task Truncated_final_snapshot_page_is_retried_without_losing_activation()
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
            ReturnEmptyCurrentPartitionSnapshotOnce = true
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
    public async Task Acquire_waits_for_contiguous_handoff_before_deciding_the_winner()
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
            PausePartitionSnapshotResponses = true
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
            var existing = ActorActivationId.New();
            Assert.True((await directoryA.AcquireAsync(
                actor,
                nodeA,
                existing,
                TestContext.Current.CancellationToken)).Acquired);

            membershipA.Current = after;
            membershipB.Current = after;
            var acquiring = directoryB.AcquireAsync(
                actor,
                nodeB,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForPartitionSnapshotResponseAsync(TestContext.Current.CancellationToken);

            Assert.False(acquiring.IsCompleted);
            network.ReleasePartitionSnapshotResponses();
            var result = await acquiring;

            Assert.False(result.Acquired);
            Assert.Equal(existing, result.Record.ActivationId);
        }
        finally
        {
            network.ReleasePartitionSnapshotResponses();
            directoryB.Dispose();
            directoryA.Dispose();
        }
    }

    [Fact]
    public async Task Release_waits_for_contiguous_handoff_before_removing_the_exact_activation()
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
            PausePartitionSnapshotResponses = true
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
            var releasing = directoryB.ReleaseAsync(
                actor,
                activation,
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForPartitionSnapshotResponseAsync(TestContext.Current.CancellationToken);

            Assert.False(releasing.IsCompleted);
            network.ReleasePartitionSnapshotResponses();

            Assert.True(await releasing);
            Assert.Null(await directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken));
        }
        finally
        {
            network.ReleasePartitionSnapshotResponses();
            directoryB.Dispose();
            directoryA.Dispose();
        }
    }

    [Fact]
    public async Task Unrelated_range_remains_available_during_contiguous_handoff()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(5, Active(nodeA), Active(nodeB));
        var movingActor = FindMovedActor(nodeB, before, after);
        var unaffectedActor = FindActorOwnedBy(nodeA, after);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(movingActor);
        var network = new DirectoryNetwork
        {
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = movingActor,
            PausePartitionSnapshotResponses = true
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
            var unaffectedActivation = ActorActivationId.New();
            Assert.True((await directoryA.AcquireAsync(
                unaffectedActor,
                nodeA,
                unaffectedActivation,
                TestContext.Current.CancellationToken)).Acquired);

            membershipA.Current = after;
            membershipB.Current = after;
            var transferring = directoryB.ResolveAsync(
                movingActor,
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForPartitionSnapshotResponseAsync(TestContext.Current.CancellationToken);

            var unaffected = await directoryB.ResolveAsync(
                unaffectedActor,
                TestContext.Current.CancellationToken);

            Assert.Equal(unaffectedActivation, unaffected!.ActivationId);
            Assert.False(transferring.IsCompleted);
            network.ReleasePartitionSnapshotResponses();
            await transferring;
        }
        finally
        {
            network.ReleasePartitionSnapshotResponses();
            directoryB.Dispose();
            directoryA.Dispose();
        }
    }

    [Fact]
    public async Task Cancelled_waiter_does_not_unlock_or_poison_a_range_in_transfer()
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
            PausePartitionSnapshotResponses = true
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
            using var cancelled = new CancellationTokenSource();
            var first = directoryB.ResolveAsync(actor, cancelled.Token).AsTask();
            await network.WaitForPartitionSnapshotResponseAsync(TestContext.Current.CancellationToken);
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            var second = directoryB.ResolveAsync(
                actor,
                TestContext.Current.CancellationToken).AsTask();
            Assert.False(second.IsCompleted);
            network.ReleasePartitionSnapshotResponses();

            Assert.Equal(activation, (await second)!.ActivationId);
        }
        finally
        {
            network.ReleasePartitionSnapshotResponses();
            directoryB.Dispose();
            directoryA.Dispose();
        }
    }

    [Fact]
    public async Task Stopping_during_transfer_fails_waiters_instead_of_exposing_partial_state()
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
            PausePartitionSnapshotResponses = true
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
            var resolving = directoryB.ResolveAsync(
                actor,
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForPartitionSnapshotResponseAsync(TestContext.Current.CancellationToken);

            await directoryB.StopAsync(TestContext.Current.CancellationToken);

            Assert.IsType<ActorDirectoryUnavailableException>(
                await Record.ExceptionAsync(() => resolving));
        }
        finally
        {
            network.ReleasePartitionSnapshotResponses();
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
    public async Task Receiver_exit_during_handoff_does_not_lose_a_surviving_activation()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB), Joining(nodeC));
        var middle = Snapshot(5, Active(nodeA), Active(nodeB), Joining(nodeC));
        var final = Snapshot(6, Active(nodeA), Active(nodeC));
        var actor = FindActorOwnedInSequence(nodeA, before, nodeB, middle, nodeC, final);
        var sourcePartition = new ActorDirectoryRing(before).GetOwner(actor);
        var activation = ActorActivationId.New();
        var registryA = new TestActorActivationSnapshotSource();
        registryA.Set(new ActorDirectoryRecord(actor, nodeA, activation, DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork
        {
            PartitionSnapshotTargetIndex = sourcePartition.Index,
            PartitionSnapshotTargetActor = actor,
            PausePartitionSnapshotResponses = true
        };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var membershipC = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, middle, registryA);
        var directoryB = Directory(nodeB, membershipB, network, middle);
        var directoryC = Directory(nodeC, membershipC, network, middle);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        network.Register(nodeC, directoryC);
        try
        {
            await Task.WhenAll(
                directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryC.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());

            membershipA.Current = middle;
            membershipB.Current = middle;
            membershipC.Current = middle;
            await directoryC.EnsureViewAsync(middle.View, TestContext.Current.CancellationToken);
            var interruptedResolve = directoryB.ResolveAsync(
                actor,
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForPartitionSnapshotResponseAsync(TestContext.Current.CancellationToken);

            directoryB.Dispose();
            network.Unregister(nodeB);
            network.ReleasePartitionSnapshotResponses();
            Assert.NotNull(await Record.ExceptionAsync(() => interruptedResolve));

            membershipA.Current = final;
            membershipC.Current = final;
            var resolved = await directoryC.ResolveAsync(actor, TestContext.Current.CancellationToken);

            Assert.Equal(activation, resolved!.ActivationId);
        }
        finally
        {
            network.ReleasePartitionSnapshotResponses();
            directoryC.Dispose();
            directoryB.Dispose();
            directoryA.Dispose();
        }
    }

    [Fact]
    public async Task Rapid_membership_churn_converges_without_losing_a_surviving_activation()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var nodeD = Reference("node-d", 4);
        var views = new[]
        {
            Snapshot(4, Active(nodeA), Joining(nodeB), Joining(nodeC), Joining(nodeD)),
            Snapshot(5, Active(nodeA), Active(nodeB), Joining(nodeC), Joining(nodeD)),
            Snapshot(6, Active(nodeA), Active(nodeB), Active(nodeC), Joining(nodeD)),
            Snapshot(7, Active(nodeA), Active(nodeC), Joining(nodeD)),
            Snapshot(8, Active(nodeA), Active(nodeC), Active(nodeD)),
            Snapshot(9, Active(nodeA), Active(nodeD))
        };
        var actor = FindActorWithOwners(views, [nodeA, nodeB, nodeC, nodeC, nodeD, nodeD]);
        var firstSourcePartition = new ActorDirectoryRing(views[0]).GetOwner(actor);
        var activation = ActorActivationId.New();
        var registryA = new TestActorActivationSnapshotSource();
        registryA.Set(new ActorDirectoryRecord(actor, nodeA, activation, DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork
        {
            PartitionSnapshotTargetIndex = firstSourcePartition.Index,
            PartitionSnapshotTargetActor = actor,
            PausePartitionSnapshotResponses = true
        };
        var memberships = new Dictionary<NodeReference, MutableMembership>
        {
            [nodeA] = new MutableMembership(views[0]),
            [nodeB] = new MutableMembership(views[0]),
            [nodeC] = new MutableMembership(views[0]),
            [nodeD] = new MutableMembership(views[0])
        };
        var directories = new Dictionary<NodeReference, DistributedActorDirectory>
        {
            [nodeA] = Directory(nodeA, memberships[nodeA], network, views[1], registryA),
            [nodeB] = Directory(nodeB, memberships[nodeB], network, views[1]),
            [nodeC] = Directory(nodeC, memberships[nodeC], network, views[1]),
            [nodeD] = Directory(nodeD, memberships[nodeD], network, views[1])
        };
        foreach (var (node, directory) in directories) network.Register(node, directory);
        try
        {
            await Task.WhenAll(directories.Values.Select(directory =>
                directory.EnsureViewAsync(views[0].View, TestContext.Current.CancellationToken).AsTask()));

            foreach (var view in views.Skip(1))
            {
                var present = view.Members.Select(static member => member.Reference).ToHashSet();
                foreach (var (node, membership) in memberships)
                {
                    if (present.Contains(node)) membership.Current = view;
                }

                await Task.WhenAll(directories
                    .Where(pair => present.Contains(pair.Key))
                    .Select(pair => pair.Value.EnsureViewAsync(
                        view.View,
                        TestContext.Current.CancellationToken).AsTask()));
                if (view.View == views[1].View)
                    await network.WaitForPartitionSnapshotResponseAsync(
                        TestContext.Current.CancellationToken);

                foreach (var removed in directories.Keys.Where(node => !present.Contains(node)).ToArray())
                {
                    directories[removed].Dispose();
                    directories.Remove(removed);
                    network.Unregister(removed);
                }
            }

            network.ReleasePartitionSnapshotResponses();
            var resolved = await directories[nodeD].ResolveAsync(
                actor,
                TestContext.Current.CancellationToken);

            Assert.Equal(activation, resolved!.ActivationId);
        }
        finally
        {
            network.ReleasePartitionSnapshotResponses();
            foreach (var directory in directories.Values) directory.Dispose();
        }
    }

    [Fact]
    public async Task Failed_handoff_recovers_the_range_still_owned_in_the_next_view()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB), Active(nodeC));
        var failed = Snapshot(6, Active(nodeA), Active(nodeB), Active(nodeC));
        var recovered = Snapshot(7, Active(nodeB), Active(nodeC));
        var actor = FindActorOwnedInSequence(nodeA, before, nodeB, failed, nodeB, recovered);
        var activation = ActorActivationId.New();
        var registryA = new TestActorActivationSnapshotSource();
        var registryC = new TestActorActivationSnapshotSource();
        registryC.Set(new ActorDirectoryRecord(
            actor,
            nodeC,
            activation,
            DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork { ActivationSnapshotTargetActor = actor };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var membershipC = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, failed, registryA);
        var directoryB = Directory(nodeB, membershipB, network, failed);
        var directoryC = Directory(nodeC, membershipC, network, failed, registryC);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        network.Register(nodeC, directoryC);
        try
        {
            await Task.WhenAll(
                directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryC.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
            registryA.Set(new ActorDirectoryRecord(
                actor,
                nodeA,
                ActorActivationId.New(),
                DateTimeOffset.UtcNow));

            membershipA.Current = failed;
            membershipB.Current = failed;
            membershipC.Current = failed;
            await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() =>
                directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken).AsTask());

            network.PauseActivationSnapshotResponses();
            membershipA.Current = recovered;
            membershipB.Current = recovered;
            membershipC.Current = recovered;
            var resolving = directoryB.ResolveAsync(
                actor,
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForActivationSnapshotResponseAsync(
                TestContext.Current.CancellationToken);

            Assert.False(resolving.IsCompleted);
            network.ReleaseActivationSnapshotResponses();
            var resolved = await resolving;

            Assert.Equal(activation, resolved!.ActivationId);
        }
        finally
        {
            network.ReleaseActivationSnapshotResponses();
            directoryC.Dispose();
            directoryB.Dispose();
            directoryA.Dispose();
        }
    }

    [Fact]
    public async Task Acquire_waits_while_a_retained_range_is_rebuilt_after_failed_handoff()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB), Active(nodeC));
        var failed = Snapshot(6, Active(nodeA), Active(nodeB), Active(nodeC));
        var recovered = Snapshot(7, Active(nodeB), Active(nodeC));
        var actor = FindActorOwnedInSequence(nodeA, before, nodeB, failed, nodeB, recovered);
        var survivingActivation = ActorActivationId.New();
        var registryA = new TestActorActivationSnapshotSource();
        var registryC = new TestActorActivationSnapshotSource();
        registryC.Set(new ActorDirectoryRecord(
            actor,
            nodeC,
            survivingActivation,
            DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork { ActivationSnapshotTargetActor = actor };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var membershipC = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, failed, registryA);
        var directoryB = Directory(nodeB, membershipB, network, failed);
        var directoryC = Directory(nodeC, membershipC, network, failed, registryC);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        network.Register(nodeC, directoryC);
        try
        {
            await Task.WhenAll(
                directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryC.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
            registryA.Set(new ActorDirectoryRecord(
                actor,
                nodeA,
                ActorActivationId.New(),
                DateTimeOffset.UtcNow));

            membershipA.Current = failed;
            membershipB.Current = failed;
            membershipC.Current = failed;
            await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() =>
                directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken).AsTask());

            network.PauseActivationSnapshotResponses();
            membershipA.Current = recovered;
            membershipB.Current = recovered;
            membershipC.Current = recovered;
            var acquiring = directoryB.AcquireAsync(
                actor,
                nodeB,
                ActorActivationId.New(),
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForActivationSnapshotResponseAsync(
                TestContext.Current.CancellationToken);

            Assert.False(acquiring.IsCompleted);
            network.ReleaseActivationSnapshotResponses();
            var result = await acquiring;

            Assert.False(result.Acquired);
            Assert.Equal(survivingActivation, result.Record.ActivationId);
        }
        finally
        {
            network.ReleaseActivationSnapshotResponses();
            directoryC.Dispose();
            directoryB.Dispose();
            directoryA.Dispose();
        }
    }

    [Fact]
    public async Task Release_waits_while_a_retained_range_is_rebuilt_after_failed_handoff()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var nodeC = Reference("node-c", 3);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB), Active(nodeC));
        var failed = Snapshot(6, Active(nodeA), Active(nodeB), Active(nodeC));
        var recovered = Snapshot(7, Active(nodeB), Active(nodeC));
        var actor = FindActorOwnedInSequence(nodeA, before, nodeB, failed, nodeB, recovered);
        var survivingActivation = ActorActivationId.New();
        var registryA = new TestActorActivationSnapshotSource();
        var registryC = new TestActorActivationSnapshotSource();
        registryC.Set(new ActorDirectoryRecord(
            actor,
            nodeC,
            survivingActivation,
            DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork { ActivationSnapshotTargetActor = actor };
        var membershipA = new MutableMembership(before);
        var membershipB = new MutableMembership(before);
        var membershipC = new MutableMembership(before);
        var directoryA = Directory(nodeA, membershipA, network, failed, registryA);
        var directoryB = Directory(nodeB, membershipB, network, failed);
        var directoryC = Directory(nodeC, membershipC, network, failed, registryC);
        network.Register(nodeA, directoryA);
        network.Register(nodeB, directoryB);
        network.Register(nodeC, directoryC);
        try
        {
            await Task.WhenAll(
                directoryA.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryB.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask(),
                directoryC.EnsureViewAsync(before.View, TestContext.Current.CancellationToken).AsTask());
            registryA.Set(new ActorDirectoryRecord(
                actor,
                nodeA,
                ActorActivationId.New(),
                DateTimeOffset.UtcNow));

            membershipA.Current = failed;
            membershipB.Current = failed;
            membershipC.Current = failed;
            await Assert.ThrowsAsync<ActorDirectoryUnavailableException>(() =>
                directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken).AsTask());

            network.PauseActivationSnapshotResponses();
            membershipA.Current = recovered;
            membershipB.Current = recovered;
            membershipC.Current = recovered;
            var releasing = directoryB.ReleaseAsync(
                actor,
                survivingActivation,
                TestContext.Current.CancellationToken).AsTask();
            await network.WaitForActivationSnapshotResponseAsync(
                TestContext.Current.CancellationToken);

            Assert.False(releasing.IsCompleted);
            network.ReleaseActivationSnapshotResponses();

            Assert.True(await releasing);
            Assert.Null(await directoryB.ResolveAsync(actor, TestContext.Current.CancellationToken));
        }
        finally
        {
            network.ReleaseActivationSnapshotResponses();
            directoryC.Dispose();
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
        var registryA = new TestActorActivationSnapshotSource();
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
    public async Task Activation_snapshot_pages_read_one_stable_catalog_snapshot_during_same_count_churn()
    {
        var node = Reference("node-a", 1);
        var snapshot = Snapshot(4, Active(node));
        var registry = new TestActorActivationSnapshotSource();
        var original = Enumerable.Range(0, 257)
            .Select(index => new ActorDirectoryRecord(
                ActorId.From($"actor/{index:D3}"),
                node,
                ActorActivationId.New(),
                DateTimeOffset.UtcNow))
            .ToArray();
        foreach (var record in original) registry.Set(record);
        var membership = new MutableMembership(snapshot);
        var directory = Directory(node, membership, new DirectoryNetwork(), snapshot, registry);
        await directory.EnsureViewAsync(snapshot.View, TestContext.Current.CancellationToken);
        var snapshotId = Guid.NewGuid();

        var first = await directory.HandleActivationSnapshotAsync(
            new ActorDirectoryActivationSnapshotRequest
            {
                View = snapshot.View.Value,
                Range = new ActorDirectoryRangeDto { Kind = 2 },
                Offset = 0,
                SnapshotId = snapshotId
            },
            TestContext.Current.CancellationToken);
        registry.Remove(original[0].ActorId);
        registry.Set(new ActorDirectoryRecord(
            ActorId.From("actor/zzz"),
            node,
            ActorActivationId.New(),
            DateTimeOffset.UtcNow));
        var second = await directory.HandleActivationSnapshotAsync(
            new ActorDirectoryActivationSnapshotRequest
            {
                View = snapshot.View.Value,
                Range = new ActorDirectoryRangeDto { Kind = 2 },
                Offset = 256,
                SnapshotId = snapshotId
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(
            original.Select(static record => record.ActorId.Value),
            first.Records.Concat(second.Records).Select(static record => record.ActorId));
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
        var registryA = new TestActorActivationSnapshotSource();
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
    public async Task Truncated_final_activation_snapshot_page_is_retried_without_losing_activation()
    {
        var nodeA = Reference("node-a", 1);
        var nodeB = Reference("node-b", 2);
        var before = Snapshot(4, Active(nodeA), Joining(nodeB));
        var after = Snapshot(6, Active(nodeA), Active(nodeB));
        var actor = FindMovedActor(nodeB, before, after);
        var activation = ActorActivationId.New();
        var registryA = new TestActorActivationSnapshotSource();
        registryA.Set(new ActorDirectoryRecord(actor, nodeA, activation, DateTimeOffset.UtcNow));
        var network = new DirectoryNetwork
        {
            ActivationSnapshotTargetActor = actor,
            ReturnEmptyCurrentActivationSnapshotOnce = true
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
        var registryA = new TestActorActivationSnapshotSource();
        var registryB = new TestActorActivationSnapshotSource();
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
        IActorActivationSnapshotSource? registry = null) => new(
        membership,
        network,
        new LocalActorNodeIdentity(local.Node.Value),
        registry,
        new RefreshingMembership(membership, refreshed));

    private sealed class TestActorActivationSnapshotSource : IActorActivationSnapshotSource
    {
        private readonly Dictionary<ActorId, ActorDirectoryRecord> records = new();

        public int ActiveCount => records.Count;

        public IReadOnlyList<ActorDirectoryRecord> CaptureRecoveryClaims() => records.Values.ToArray();

        public void Set(ActorDirectoryRecord record) => records[record.ActorId] = record;

        public void Remove(ActorId actorId) => records.Remove(actorId);
    }

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

    private static ActorId FindActorOwnedInSequence(
        NodeReference firstOwner,
        ClusterMembershipSnapshot first,
        NodeReference secondOwner,
        ClusterMembershipSnapshot second,
        NodeReference thirdOwner,
        ClusterMembershipSnapshot third)
    {
        var firstRing = new ActorDirectoryRing(first);
        var secondRing = new ActorDirectoryRing(second);
        var thirdRing = new ActorDirectoryRing(third);
        for (var index = 0; index < 1_000_000; index++)
        {
            var actor = ActorId.From($"churn-room/{index}");
            if (firstRing.GetOwner(actor).Owner == firstOwner
                && secondRing.GetOwner(actor).Owner == secondOwner
                && thirdRing.GetOwner(actor).Owner == thirdOwner)
                return actor;
        }

        throw new InvalidOperationException("No Actor id followed the expected owner sequence.");
    }

    private static ActorId FindActorWithOwners(
        IReadOnlyList<ClusterMembershipSnapshot> views,
        IReadOnlyList<NodeReference> expectedOwners)
    {
        Assert.Equal(views.Count, expectedOwners.Count);
        var rings = views.Select(view => new ActorDirectoryRing(view)).ToArray();
        for (var index = 0; index < 1_000_000; index++)
        {
            var actor = ActorId.From($"rapid-churn-room/{index}");
            var owners = rings.Select(ring => ring.GetOwner(actor).Owner).ToArray();
            if (owners.SequenceEqual(expectedOwners)) return actor;
        }

        throw new InvalidOperationException("No Actor id followed the expected churn owner sequence.");
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
        private readonly TaskCompletionSource activationSnapshotResponseObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseActivationSnapshotResponses =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ActorDirectorySnapshotReply? firstPartitionSnapshotPage;
        private int replayFirstPartitionSnapshotPage = 1;
        private int truncateFirstPartitionSnapshotPage = 1;
        private int returnStaleEmptyPartitionSnapshot = 1;
        private int returnEmptyCurrentPartitionSnapshot = 1;
        private int returnStaleEmptyActivationSnapshot = 1;
        private int returnEmptyCurrentActivationSnapshot = 1;
        private int pauseActivationSnapshotResponses;

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

        public bool ReturnEmptyCurrentPartitionSnapshotOnce { get; init; }

        public ActorId? ActivationSnapshotTargetActor { get; init; }

        public bool ReturnStaleEmptyActivationSnapshotOnce { get; init; }

        public bool ReturnEmptyCurrentActivationSnapshotOnce { get; init; }

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

        public void PauseActivationSnapshotResponses() =>
            Volatile.Write(ref pauseActivationSnapshotResponses, 1);

        public Task WaitForActivationSnapshotResponseAsync(CancellationToken cancellationToken) =>
            activationSnapshotResponseObserved.Task.WaitAsync(cancellationToken);

        public void ReleaseActivationSnapshotResponses() =>
            releaseActivationSnapshotResponses.TrySetResult();

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
            if (ReturnEmptyCurrentPartitionSnapshotOnce
                && Interlocked.Exchange(ref returnEmptyCurrentPartitionSnapshot, 0) == 1)
                return new ActorDirectorySnapshotReply
                {
                    Available = true,
                    View = request.View,
                    Records = [],
                    HasMore = false,
                    TotalCount = reply.TotalCount
                };
            if (ReturnStaleEmptyPartitionSnapshotOnce
                && Interlocked.Exchange(ref returnStaleEmptyPartitionSnapshot, 0) == 1)
                return new ActorDirectorySnapshotReply
                {
                    Available = true,
                    View = request.View - 1,
                    Records = [],
                    HasMore = false,
                    TotalCount = reply.TotalCount
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
                        HasMore = reply.HasMore,
                        TotalCount = reply.TotalCount
                    };
                return reply;
            }

            return request.Offset == ReplayFirstPartitionSnapshotPageAtOffset
                && Interlocked.Exchange(ref replayFirstPartitionSnapshotPage, 0) == 1
                ? firstPartitionSnapshotPage ?? reply
                : reply;
        }

        public ActorDirectorySnapshotReply AlterActivationSnapshotIfRequested(
            ActorDirectoryActivationSnapshotRequest request,
            ActorDirectorySnapshotReply reply)
        {
            if (ActivationSnapshotTargetActor is not { } actor
                || !Range(request.Range).Contains(actor))
                return reply;
            if (ReturnEmptyCurrentActivationSnapshotOnce
                && Interlocked.Exchange(ref returnEmptyCurrentActivationSnapshot, 0) == 1)
                return new ActorDirectorySnapshotReply
                {
                    Available = true,
                    View = request.View,
                    Records = [],
                    HasMore = false,
                    TotalCount = reply.TotalCount
                };
            if (!ReturnStaleEmptyActivationSnapshotOnce
                || Interlocked.Exchange(ref returnStaleEmptyActivationSnapshot, 0) != 1)
                return reply;
            return new ActorDirectorySnapshotReply
            {
                Available = true,
                View = request.View - 1,
                Records = [],
                HasMore = false,
                TotalCount = reply.TotalCount
            };
        }

        public async ValueTask PauseActivationSnapshotResponseAsync(
            ActorDirectoryActivationSnapshotRequest request,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref pauseActivationSnapshotResponses) == 0
                || ActivationSnapshotTargetActor is not { } actor
                || !Range(request.Range).Contains(actor))
                return;
            activationSnapshotResponseObserved.TrySetResult();
            await releaseActivationSnapshotResponses.Task.WaitAsync(cancellationToken);
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

        public void Unregister(NodeReference node) => directories.Remove(node);

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
                await network.PauseActivationSnapshotResponseAsync(
                    activationSnapshotRequest,
                    ct);
                reply = network.AlterActivationSnapshotIfRequested(
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
