using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lakona.Game.Testing.Tests;

public sealed class LakonaTestClusterTests
{
    [Fact]
    public void BuilderRequiresAnInitialNode()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new LakonaTestClusterBuilder().Build());

        Assert.Contains("at least one", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuilderRejectsDuplicateNodeIds()
    {
        var builder = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddNode("data-1", "battle"));

        Assert.Contains("data-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleNodeStartsAndStops()
    {
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .Build();

        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var snapshot = await cluster.WaitForMembershipAsync(
            TestContext.Current.CancellationToken);

        var member = Assert.Single(snapshot.Members);
        Assert.Equal("data-1", member.Reference.Node.Value);
        Assert.Equal(ClusterMemberState.Active, member.State);
    }

    [Fact]
    public async Task StartsIndependentNodesAgainstOneMembershipTable()
    {
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("battle-1", "battle")
            .ConfigureNodes(node =>
            {
                if (node.HasRole("data"))
                {
                    node.ConfigureAppConfiguration(configuration =>
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:GameDatabase"] = "test-database"
                        }));
                }

                node.ConfigureServices((services, configuration) =>
                    services.AddSingleton(new NodeConfigurationProbe(
                        node.NodeId,
                        configuration.GetConnectionString("GameDatabase"))));
            })
            .Build();

        await cluster.StartAsync(TestContext.Current.CancellationToken);
        var snapshot = await cluster.WaitForMembershipAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(2, cluster.Nodes.Count);
        Assert.Equal(2, snapshot.Members.Count);
        Assert.All(snapshot.Members, member =>
            Assert.Equal(ClusterMemberState.Active, member.State));
        Assert.Single(snapshot.Members.Select(member => member.Reference.Cluster).Distinct());

        var data = cluster.Node("data-1");
        var battle = cluster.Node("battle-1");
        Assert.NotSame(data.Services, battle.Services);
        Assert.Equal(["data"], data.Roles);
        Assert.Equal(["battle"], battle.Roles);
        Assert.Equal("test-database", data.Services.GetRequiredService<NodeConfigurationProbe>().ConnectionString);
        Assert.Null(battle.Services.GetRequiredService<NodeConfigurationProbe>().ConnectionString);
        Assert.NotNull(data.Services.GetRequiredService<IActorDirectory>());
        Assert.NotNull(battle.Services.GetRequiredService<IActorDirectory>());
    }

    [Fact]
    public async Task GracefulStopConvergesRemainingNodes()
    {
        await using var cluster = TwoNodeCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);

        var stopped = await cluster.StopNodeAsync(
            "battle-1",
            TestContext.Current.CancellationToken);
        var snapshot = await cluster.WaitForMembershipAsync(
            TestContext.Current.CancellationToken);

        Assert.False(stopped.IsActive);
        var member = Assert.Single(snapshot.Members);
        Assert.Equal("data-1", member.Reference.Node.Value);
        Assert.Equal(ClusterMemberState.Active, member.State);
    }

    [Fact]
    public async Task KilledNodeCanRestartWithANewIncarnation()
    {
        await using var cluster = TwoNodeCluster();
        await cluster.StartAsync(TestContext.Current.CancellationToken);
        await cluster.WaitForMembershipAsync(TestContext.Current.CancellationToken);
        var previous = cluster.Node("battle-1");

        await cluster.KillNodeAsync(
            "battle-1",
            TestContext.Current.CancellationToken);
        var replacement = await cluster.StartNodeAsync(
            "battle-1",
            TestContext.Current.CancellationToken);
        var snapshot = await cluster.WaitForMembershipAsync(
            TestContext.Current.CancellationToken);

        Assert.False(previous.IsActive);
        Assert.True(replacement.IsActive);
        Assert.Equal(previous.Reference.Node, replacement.Reference.Node);
        Assert.NotEqual(previous.Reference.Incarnation, replacement.Reference.Incarnation);
        Assert.Contains(snapshot.Members, member => member.Reference == replacement.Reference);
        Assert.DoesNotContain(snapshot.Members, member => member.Reference == previous.Reference);
    }

    [Fact]
    public async Task AdditionalNodeUsesTheSharedConfigurationPipeline()
    {
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .ConfigureNodes(node =>
                node.ConfigureServices((services, _) =>
                    services.AddSingleton(new NodeConfigurationProbe(node.NodeId, null))))
            .Build();
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        var gateway = await cluster.StartAdditionalNodeAsync(
            "gateway-1",
            ["gateway"],
            TestContext.Current.CancellationToken);
        var snapshot = await cluster.WaitForMembershipAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("gateway-1", gateway.Services
            .GetRequiredService<NodeConfigurationProbe>().NodeId);
        Assert.Equal(2, snapshot.Members.Count);
    }

    [Fact]
    public async Task FailedNodeStartupStopsNodesWhichAlreadyStarted()
    {
        await using var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("battle-1", "battle")
            .ConfigureNodes(node =>
            {
                if (node.NodeId == "battle-1")
                {
                    node.ConfigureServices((services, _) =>
                        services.AddSingleton<IHostedService, FailingHostedService>());
                }
            })
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cluster.StartAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(cluster.Nodes, static node => node.IsActive);
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var cluster = new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .Build();
        await cluster.StartAsync(TestContext.Current.CancellationToken);

        await cluster.DisposeAsync();
        await cluster.DisposeAsync();
    }

    private static LakonaTestCluster TwoNodeCluster() =>
        new LakonaTestClusterBuilder()
            .AddNode("data-1", "data")
            .AddNode("battle-1", "battle")
            .Build();

    private sealed record NodeConfigurationProbe(string NodeId, string? ConnectionString);

    private sealed class FailingHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Expected startup failure."));

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
