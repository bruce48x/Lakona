using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class ActorDirectoryTests
{
    [Fact]
    public async Task RegisterAsync_adds_record_resolvable_by_actor_id()
    {
        var directory = new InMemoryActorDirectory();
        var actorId = ActorId.From("room/1001");
        var node = new NodeId("node-a");

        var status = await directory.RegisterAsync(actorId, node, TestContext.Current.CancellationToken);
        var record = await directory.ResolveAsync(actorId, TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryRegisterStatus.Registered, status);
        Assert.NotNull(record);
        Assert.Equal(actorId, record.ActorId);
        Assert.Equal(node, record.Node);
        Assert.True(record.Version > 0);
    }

    [Fact]
    public async Task RegisterAsync_is_idempotent_for_same_actor_and_node()
    {
        var directory = new InMemoryActorDirectory();
        var actorId = ActorId.From("room/1001");
        var node = new NodeId("node-a");

        var first = await directory.RegisterAsync(actorId, node, TestContext.Current.CancellationToken);
        var second = await directory.RegisterAsync(actorId, node, TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryRegisterStatus.Registered, first);
        Assert.Equal(ActorDirectoryRegisterStatus.AlreadyRegistered, second);
    }

    [Fact]
    public async Task RegisterAsync_rejects_different_node_for_existing_actor()
    {
        var directory = new InMemoryActorDirectory();
        var actorId = ActorId.From("room/1001");
        var originalNode = new NodeId("node-a");
        var competingNode = new NodeId("node-b");

        await directory.RegisterAsync(actorId, originalNode, TestContext.Current.CancellationToken);

        var status = await directory.RegisterAsync(actorId, competingNode, TestContext.Current.CancellationToken);
        var record = await directory.ResolveAsync(actorId, TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryRegisterStatus.Conflict, status);
        Assert.NotNull(record);
        Assert.Equal(originalNode, record.Node);
    }

    [Fact]
    public async Task UnregisterAsync_removes_record_when_node_matches()
    {
        var directory = new InMemoryActorDirectory();
        var actorId = ActorId.From("room/1001");
        var node = new NodeId("node-a");

        await directory.RegisterAsync(actorId, node, TestContext.Current.CancellationToken);

        var status = await directory.UnregisterAsync(actorId, node, TestContext.Current.CancellationToken);
        var record = await directory.ResolveAsync(actorId, TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryUnregisterStatus.Unregistered, status);
        Assert.Null(record);
    }

    [Fact]
    public async Task UnregisterAsync_returns_not_found_for_missing_actor()
    {
        var directory = new InMemoryActorDirectory();

        var status = await directory.UnregisterAsync(
            ActorId.From("room/1001"),
            new NodeId("node-a"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryUnregisterStatus.NotFound, status);
    }

    [Fact]
    public async Task UnregisterAsync_rejects_mismatched_node_without_removing_record()
    {
        var directory = new InMemoryActorDirectory();
        var actorId = ActorId.From("room/1001");
        var ownerNode = new NodeId("node-a");
        var wrongNode = new NodeId("node-b");

        await directory.RegisterAsync(actorId, ownerNode, TestContext.Current.CancellationToken);

        var status = await directory.UnregisterAsync(actorId, wrongNode, TestContext.Current.CancellationToken);
        var record = await directory.ResolveAsync(actorId, TestContext.Current.CancellationToken);

        Assert.Equal(ActorDirectoryUnregisterStatus.OwnershipMismatch, status);
        Assert.NotNull(record);
        Assert.Equal(ownerNode, record.Node);
    }
}
