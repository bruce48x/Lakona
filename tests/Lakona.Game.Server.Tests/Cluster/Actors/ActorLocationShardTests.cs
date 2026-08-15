using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Tests.Testing;
using Xunit;

namespace Lakona.Game.Server.Tests.Cluster.Actors;

public sealed class ActorLocationShardTests
{
    [Fact]
    public void Recovery_conflict_records_actor_location_conflict()
    {
        using var metrics = new MetricReasonCollector(
            ClusterDiagnostics.MeterName,
            "lakona.game.cluster.actor_location.failure",
            "lakona.game.cluster.reason");
        var owner = Reference("node-a", 1);
        var actor = ActorId.From("room/conflict");
        var shard = new ActorLocationShard(owner, new MembershipViewId(4));

        Assert.Throws<ActorDirectoryUnavailableException>(() => shard.Restore(
        [
            new ActorDirectoryRecord(actor, owner, ActorActivationId.New(), DateTimeOffset.UnixEpoch),
            new ActorDirectoryRecord(actor, owner, ActorActivationId.New(), DateTimeOffset.UnixEpoch)
        ]));

        Assert.Contains("conflict", metrics.Reasons);
    }

    [Fact]
    public void Recovery_capacity_exhaustion_records_actor_location_capacity()
    {
        using var metrics = new MetricReasonCollector(
            ClusterDiagnostics.MeterName,
            "lakona.game.cluster.actor_location.failure",
            "lakona.game.cluster.reason");
        var owner = Reference("node-a", 1);
        var shard = new ActorLocationShard(owner, new MembershipViewId(4));
        var records = Enumerable.Range(0, ActorLocationShard.MaximumRecords + 1)
            .Select(index => new ActorDirectoryRecord(
                ActorId.From($"room/{index}"),
                owner,
                ActorActivationId.New(),
                DateTimeOffset.UnixEpoch))
            .ToArray();

        Assert.Throws<ActorDirectoryUnavailableException>(() => shard.Restore(records));

        Assert.Contains("capacity", metrics.Reasons);
    }

    [Fact]
    public void Descriptor_only_membership_progress_does_not_reject_the_same_exact_owner()
    {
        var owner = Reference("node-a", 1);
        var shard = new ActorLocationShard(owner, new MembershipViewId(4));
        var actor = ActorId.From("room/42");
        var activation = new ActorActivationId(Guid.Parse("20000000-0000-0000-0000-000000000000"));

        var registered = shard.Register(actor, owner, activation, owner, new MembershipViewId(4));
        var resolved = shard.Lookup(actor, owner, new MembershipViewId(5));

        Assert.Equal(ActorLocationMutationStatus.Applied, registered.Status);
        Assert.Equal(activation, resolved.Record!.ActivationId);
        Assert.Equal(new MembershipViewId(5), shard.ObservedView);
    }

    [Fact]
    public void Delayed_request_from_before_descriptor_only_progress_uses_the_same_exact_owner()
    {
        var owner = Reference("node-a", 1);
        var shard = new ActorLocationShard(owner, new MembershipViewId(5));
        var actor = ActorId.From("room/42");
        var activation = ActorActivationId.New();

        var registered = shard.Register(actor, owner, activation, owner, new MembershipViewId(4));
        var resolved = shard.Lookup(actor, owner, new MembershipViewId(4));

        Assert.Equal(ActorLocationMutationStatus.Applied, registered.Status);
        Assert.Equal(activation, resolved.Record!.ActivationId);
    }

    [Fact]
    public void Replaced_incarnation_is_redirected_before_mutation()
    {
        var oldOwner = Reference("node-a", 1);
        var newOwner = Reference("node-a", 2);
        var shard = new ActorLocationShard(newOwner, new MembershipViewId(5));

        var result = shard.Register(
            ActorId.From("room/42"),
            oldOwner,
            ActorActivationId.New(),
            oldOwner,
            new MembershipViewId(4));

        Assert.Equal(ActorLocationMutationStatus.RefreshRequired, result.Status);
        Assert.Equal(newOwner, result.Owner);
    }

    [Fact]
    public void Skipped_view_forces_recovery_even_when_exact_owner_returns()
    {
        var owner = Reference("node-a", 1);
        var shard = new ActorLocationShard(owner, new MembershipViewId(4));

        Assert.False(shard.TryAdvanceStableOwner(owner, new MembershipViewId(6)));
        Assert.Equal(new MembershipViewId(4), shard.ObservedView);
    }

    [Fact]
    public void Consecutive_descriptor_only_view_keeps_same_owner_serving()
    {
        var owner = Reference("node-a", 1);
        var shard = new ActorLocationShard(owner, new MembershipViewId(4));

        Assert.True(shard.TryAdvanceStableOwner(owner, new MembershipViewId(5)));
        Assert.Equal(new MembershipViewId(5), shard.ObservedView);
    }

    [Fact]
    public void Conditional_registration_and_unregister_preserve_one_exact_activation()
    {
        var owner = Reference("node-a", 1);
        var host = Reference("host-a", 1);
        var shard = new ActorLocationShard(owner, new MembershipViewId(4));
        var actor = ActorId.From("room/42");
        var first = ActorActivationId.New();
        var second = ActorActivationId.New();

        Assert.Equal(ActorLocationMutationStatus.Applied,
            shard.Register(actor, host, first, owner, new MembershipViewId(4)).Status);
        Assert.Equal(first,
            shard.Register(actor, host, second, owner, new MembershipViewId(4)).Record!.ActivationId);
        Assert.Equal(ActorLocationMutationStatus.ConditionFailed,
            shard.Unregister(actor, second, new MembershipViewId(4)).Status);
        Assert.Equal(ActorLocationMutationStatus.Applied,
            shard.Unregister(actor, first, new MembershipViewId(4)).Status);
        Assert.Null(shard.Lookup(actor, owner, new MembershipViewId(4)).Record);
    }

    private static NodeReference Reference(string node, int incarnation) => new(
        new ClusterIncarnationId(Guid.Parse("10000000-0000-0000-0000-000000000000")),
        new NodeId(node),
        new NodeIncarnationId(Guid.Parse($"{incarnation:D8}-0000-0000-0000-000000000000")));

}
