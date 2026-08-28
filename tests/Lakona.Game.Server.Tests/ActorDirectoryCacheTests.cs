using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ActorDirectoryCacheTests
{
    [Fact]
    public void Exact_record_is_evicted_when_owner_incarnation_leaves_membership()
    {
        var cluster = new ClusterIncarnationId(Guid.NewGuid());
        var owner = new NodeReference(cluster, new NodeId("node-a"), NodeIncarnationId.New());
        var membership = new MutableMembership(new ClusterMembershipSnapshot(
            cluster,
            new MembershipViewId(1),
            [new ClusterMember(owner, ClusterMemberState.Active, new NodeEndpoint("tcp://127.0.0.1:21001"))]));
        var cache = new InMemoryActorDirectoryCache(membership);
        var actorId = ActorId.From("room/1001");
        cache.Set(new ActorDirectoryRecord(actorId, owner, ActorActivationId.New(), DateTimeOffset.UtcNow));
        membership.Current = new ClusterMembershipSnapshot(cluster, new MembershipViewId(2), []);

        Assert.False(cache.TryGet(actorId, out _));
        Assert.False(cache.TryGetRecord(actorId, out _));
    }

    [Fact]
    public void TryGet_returns_cached_node()
    {
        var cache = new InMemoryActorDirectoryCache();
        var actorId = ActorId.From("room/1001");
        var node = new NodeId("node-a");

        cache.Set(actorId, node);

        Assert.True(cache.TryGet(actorId, out var cachedNode));
        Assert.Equal(node, cachedNode);
    }

    [Fact]
    public void Set_replaces_existing_node()
    {
        var cache = new InMemoryActorDirectoryCache();
        var actorId = ActorId.From("room/1001");

        cache.Set(actorId, new NodeId("node-a"));
        cache.Set(actorId, new NodeId("node-b"));

        Assert.True(cache.TryGet(actorId, out var cachedNode));
        Assert.Equal(new NodeId("node-b"), cachedNode);
    }

    [Fact]
    public void Remove_invalidates_cached_node()
    {
        var cache = new InMemoryActorDirectoryCache();
        var actorId = ActorId.From("room/1001");

        cache.Set(actorId, new NodeId("node-a"));
        cache.Remove(actorId);

        Assert.False(cache.TryGet(actorId, out _));
    }

    [Fact]
    public void TryGet_returns_false_for_missing_actor()
    {
        var cache = new InMemoryActorDirectoryCache();

        Assert.False(cache.TryGet(ActorId.From("room/1001"), out _));
    }

    [Fact]
    public void Concurrent_lookup_release_and_replacement_never_return_a_torn_record()
    {
        var cache = new InMemoryActorDirectoryCache();
        var cluster = new ClusterIncarnationId(Guid.NewGuid());
        var oldOwner = new NodeReference(cluster, new NodeId("node-a"), NodeIncarnationId.New());
        var newOwner = new NodeReference(cluster, new NodeId("node-b"), NodeIncarnationId.New());
        var actor = ActorId.From("room/cache-race");
        var oldRecord = new ActorDirectoryRecord(
            actor,
            oldOwner,
            ActorActivationId.New(),
            DateTimeOffset.UtcNow);
        var replacement = new ActorDirectoryRecord(
            actor,
            newOwner,
            ActorActivationId.New(),
            DateTimeOffset.UtcNow);
        var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();

        Parallel.For(0, 20_000, index =>
        {
            switch (index % 4)
            {
                case 0:
                    cache.Set(oldRecord);
                    break;
                case 1:
                    cache.Set(replacement);
                    break;
                case 2:
                    cache.Remove(actor);
                    break;
                default:
                    if (cache.TryGetRecord(actor, out var record)
                        && record != oldRecord
                        && record != replacement)
                    {
                        failures.Enqueue($"Unexpected activation {record!.ActivationId.Value}.");
                    }
                    break;
            }
        });

        cache.Set(replacement);
        Assert.Empty(failures);
        Assert.True(cache.TryGetRecord(actor, out var final));
        Assert.Equal(replacement, final);
        Assert.True(cache.TryGet(actor, out var node));
        Assert.Equal(replacement.Node, node);
    }

    private sealed class MutableMembership(ClusterMembershipSnapshot current) : IClusterMembership
    {
        public ClusterMembershipSnapshot Current { get; set; } = current;

        public ValueTask<ClusterMembershipSnapshot> WaitForChangeAsync(
            MembershipViewId after,
            CancellationToken cancellationToken = default) => new(Current);
    }
}
