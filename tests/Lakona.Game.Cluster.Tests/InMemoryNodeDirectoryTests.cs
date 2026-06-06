using Lakona.Game.Cluster;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class InMemoryNodeDirectoryTests
{
    [Fact]
    public async Task RegisterAssignsEpochOneForNewNode()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;

        var result = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeRegistrationStatus.Registered, result.Status);
        Assert.NotNull(result.Record);
        Assert.Equal(1, result.Record!.NodeEpoch);
    }

    [Fact]
    public async Task RegisterIncrementsEpochForRestartedNode()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;

        var first = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        var second = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now.AddSeconds(1)),
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Record!.NodeEpoch);
        Assert.Equal(2, second.Record!.NodeEpoch);
    }

    [Fact]
    public async Task HeartbeatRejectsOldEpoch()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now.AddSeconds(1)),
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var status = await directory.HeartbeatAsync(
            "local",
            "node-a",
            1,
            now.AddSeconds(40),
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeHeartbeatStatus.EpochMismatch, status);
    }

    [Fact]
    public async Task QueryFiltersByServiceKind()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            TestRegistration("local", "gateway-1", now, "gateway"),
            now,
            TestContext.Current.CancellationToken);
        await directory.RegisterAsync(
            TestRegistration("local", "room-1", now, "room"),
            now,
            TestContext.Current.CancellationToken);

        var records = await directory.QueryAsync(
            new NodeDirectoryQuery("local", serviceKind: "room"),
            now,
            TestContext.Current.CancellationToken);

        Assert.Single(records);
        Assert.Equal("room-1", records[0].NodeId.Value);
    }

    [Fact]
    public async Task ExpireMarksExpiredNodesDead()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;
        await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);

        var removed = await directory.ExpireAsync(
            "local",
            now.AddMinutes(1),
            TestContext.Current.CancellationToken);
        var record = await directory.ResolveAsync(
            "local",
            "node-a",
            now.AddMinutes(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, removed);
        Assert.Null(record);
    }

    [Fact]
    public async Task ResolveReturnsNullForDeadUnexpiredNode()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;
        var registration = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.UpdateStateAsync(
            "local",
            "node-a",
            registration.Record!.NodeEpoch,
            NodeState.Dead,
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var record = await directory.ResolveAsync(
            "local",
            "node-a",
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Null(record);
    }

    [Fact]
    public async Task QueryExcludesDeadUnexpiredNodeUnlessIncludingExpired()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;
        var registration = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.UpdateStateAsync(
            "local",
            "node-a",
            registration.Record!.NodeEpoch,
            NodeState.Dead,
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var activeRecords = await directory.QueryAsync(
            new NodeDirectoryQuery("local"),
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);
        var allRecords = await directory.QueryAsync(
            new NodeDirectoryQuery("local", includeExpired: true),
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Empty(activeRecords);
        var record = Assert.Single(allRecords);
        Assert.Equal(NodeState.Dead, record.State);
    }

    [Fact]
    public async Task HeartbeatReturnsExpiredForDeadUnexpiredNode()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;
        var registration = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.UpdateStateAsync(
            "local",
            "node-a",
            registration.Record!.NodeEpoch,
            NodeState.Dead,
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var status = await directory.HeartbeatAsync(
            "local",
            "node-a",
            registration.Record.NodeEpoch,
            now.AddSeconds(40),
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeHeartbeatStatus.Expired, status);
    }

    [Fact]
    public async Task UpdateStateReturnsExpiredForDeadUnexpiredNode()
    {
        var directory = new InMemoryNodeDirectory();
        var now = DateTimeOffset.UtcNow;
        var registration = await directory.RegisterAsync(
            TestRegistration("local", "node-a", now),
            now,
            TestContext.Current.CancellationToken);
        await directory.UpdateStateAsync(
            "local",
            "node-a",
            registration.Record!.NodeEpoch,
            NodeState.Dead,
            now.AddSeconds(1),
            TestContext.Current.CancellationToken);

        var status = await directory.UpdateStateAsync(
            "local",
            "node-a",
            registration.Record.NodeEpoch,
            NodeState.Ready,
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);
        var records = await directory.QueryAsync(
            new NodeDirectoryQuery("local", includeExpired: true),
            now.AddSeconds(2),
            TestContext.Current.CancellationToken);

        Assert.Equal(NodeStateUpdateStatus.Expired, status);
        Assert.Equal(NodeState.Dead, Assert.Single(records).State);
    }

    private static NodeRegistration TestRegistration(
        string clusterName,
        string nodeId,
        DateTimeOffset now,
        string serviceKind = "gateway")
    {
        return new NodeRegistration(
            clusterName,
            nodeId,
            new Dictionary<string, NodeEndpoint>
            {
                ["cluster"] = new NodeEndpoint($"tcp://127.0.0.1:{21000 + Math.Abs(nodeId.GetHashCode() % 1000)}")
            },
            new[]
            {
                new NodeServiceDescriptor(serviceKind)
            },
            now.AddSeconds(30),
            NodeState.Ready);
    }
}
