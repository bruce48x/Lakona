using Lakona.Game.Cluster;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LocalClusterNodeDiscoveryTests
{
    [Fact]
    public async Task QueryProjectsCurrentLocalStartupDescriptorsWithoutRegistrationOrLeases()
    {
        var startupCatalog = new StartupActorDescriptorCatalog(
        [
            new StartupActorDescriptor("matchmaking", "policy-a", "build-a")
        ]);
        var discovery = new LocalClusterNodeDiscovery(
            new LakonaGameRuntimeOptions
            {
                Node = new LakonaGameNodeOptions { Id = "gateway-1" }
            },
            new ActorHostDescriptorCatalog([]),
            startupCatalog);

        var nodes = await discovery.QueryAsync(
            new ClusterNodeDiscoveryQuery(
                startupActorName: "matchmaking",
                startupActorPolicyHash: "policy-a"),
            TestContext.Current.CancellationToken);

        var node = Assert.Single(nodes);
        Assert.Equal(new NodeId("gateway-1"), node.Node);
        Assert.Null(node.Reference);
        Assert.Equal("tcp://127.0.0.1:21001", node.Endpoints["cluster"].Address);

        startupCatalog.Replace([]);
        Assert.Empty(await discovery.QueryAsync(
            new ClusterNodeDiscoveryQuery(startupActorName: "matchmaking"),
            TestContext.Current.CancellationToken));
    }
}
