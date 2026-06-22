using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Sql;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hosting;
using Microsoft.Extensions.Configuration;
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
        Assert.IsType<InMemoryRouteDirectory>(provider.GetRequiredService<IRouteDirectory>());
    }

    [Fact]
    public void AddLakonaGameClusterEndpoint_registers_sql_node_directory_from_runtime_directory_options()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LakonaClusterPostgres"] = "Host=postgres;Database=lakona-game",
                ["Lakona:Node:Id"] = "data-1",
                ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21001",
                ["Lakona:Cluster:Directory:Provider"] = "postgres",
                ["Lakona:Cluster:Directory:ConnectionStringName"] = "LakonaClusterPostgres",
                ["Lakona:Cluster:Directory:NodeTable"] = "lakona_cluster_nodes"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(LakonaGameRuntimeOptions.FromConfiguration(configuration));

        services.AddLakonaGameClusterEndpoint();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SqlNodeDirectory>(provider.GetRequiredService<INodeDirectory>());
        var sqlOptions = provider.GetRequiredService<SqlNodeDirectoryOptions>();
        Assert.Equal(SqlNodeDirectoryDialect.Postgres, sqlOptions.Dialect);
        Assert.Equal("lakona_cluster_nodes", sqlOptions.TableName);
    }

    [Fact]
    public void AddLakonaGame_registers_sql_node_directory_without_pre_registered_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:LakonaClusterPostgres"] = "Host=postgres;Database=lakona-game",
                ["Lakona:Node:Id"] = "data-1",
                ["Lakona:Cluster:Endpoint"] = "tcp://127.0.0.1:21001",
                ["Lakona:Cluster:Directory:Provider"] = "postgres",
                ["Lakona:Cluster:Directory:ConnectionStringName"] = "LakonaClusterPostgres"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLakonaGame(configuration, _ => { });
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SqlNodeDirectory>(provider.GetRequiredService<INodeDirectory>());
        Assert.NotNull(provider.GetRequiredService<SqlNodeDirectoryOptions>());
    }
}
