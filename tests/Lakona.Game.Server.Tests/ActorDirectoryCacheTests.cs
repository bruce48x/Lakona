using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ActorDirectoryCacheTests
{
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
}
