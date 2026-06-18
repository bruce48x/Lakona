using Lakona.Game.Cluster;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Server.Tests.Hosting;

public sealed class LakonaClusterEndpointServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLakonaGameClusterEndpoint_registers_cluster_node_discovery()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new LakonaGameRuntimeOptions
        {
            Node = new LakonaGameNodeOptions { Id = "data-1" },
            Cluster = new LakonaGameClusterOptions
            {
                Endpoint = "tcp://127.0.0.1:21001",
                Seeds = ["tcp://127.0.0.1:21001"]
            }
        });
        services.AddSingleton<INodeDirectory, InMemoryNodeDirectory>();

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<ClusterNodeDiscovery>(provider.GetRequiredService<IClusterNodeDiscovery>());
    }
}
