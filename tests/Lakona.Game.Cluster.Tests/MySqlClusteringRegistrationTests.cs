using Lakona.Game.Cluster.Membership;
using Lakona.Game.Clustering.MySql;
using Lakona.Game.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class MySqlClusteringRegistrationTests
{
    [Fact]
    public async Task SelectedProviderReplacesTheBuiltInMemoryTable()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Lakona:Cluster:Membership:Provider"] = MySqlClusteringServiceCollectionExtensions.ProviderName,
            ["Lakona:Cluster:Membership:ConnectionStringName"] = "LakonaClusterMySql",
            ["ConnectionStrings:LakonaClusterMySql"] = "Server=127.0.0.1;Database=lakona;User ID=lakona;Password=test"
        });
        var services = new ServiceCollection();
        services.AddLakonaGameServer(configuration);
        services.AddLakonaMySqlClustering(configuration);

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<MySqlMembershipTable>(provider.GetRequiredService<IMembershipTable>());
    }

    [Fact]
    public void UnselectedProviderLeavesTheBuiltInMemoryTable()
    {
        var configuration = Configuration([]);
        var services = new ServiceCollection();
        services.AddLakonaGameServer(configuration);
        services.AddLakonaMySqlClustering(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<InMemoryMembershipTable>(provider.GetRequiredService<IMembershipTable>());
    }

    [Fact]
    public void SelectedProviderRequiresItsNamedConnection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Lakona:Cluster:Membership:Provider"] = MySqlClusteringServiceCollectionExtensions.ProviderName,
            ["Lakona:Cluster:Membership:ConnectionStringName"] = "LakonaClusterMySql"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddLakonaMySqlClustering(configuration));

        Assert.Contains("ConnectionStrings:LakonaClusterMySql", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
