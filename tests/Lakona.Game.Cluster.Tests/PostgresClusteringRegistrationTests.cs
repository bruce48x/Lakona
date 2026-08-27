using Lakona.Game.Cluster.Membership;
using Lakona.Game.Clustering.Postgres;
using Lakona.Game.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class PostgresClusteringRegistrationTests
{
    [Fact]
    public async Task SelectedProviderReplacesTheBuiltInMemoryTable()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Lakona:Cluster:Membership:Provider"] = PostgresClusteringServiceCollectionExtensions.ProviderName,
            ["ConnectionStrings:LakonaClusterPostgres"] = "Host=127.0.0.1;Database=lakona"
        });
        var services = new ServiceCollection();
        services.AddLakonaGameServer(configuration);
        services.AddLakonaPostgresClustering(configuration);

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<PostgresMembershipTable>(provider.GetRequiredService<IMembershipTable>());
    }

    [Fact]
    public void UnselectedProviderLeavesTheBuiltInMemoryTable()
    {
        var configuration = Configuration([]);
        var services = new ServiceCollection();
        services.AddLakonaGameServer(configuration);
        services.AddLakonaPostgresClustering(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<InMemoryMembershipTable>(provider.GetRequiredService<IMembershipTable>());
    }

    [Fact]
    public void SelectedProviderRequiresItsNamedConnection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Lakona:Cluster:Membership:Provider"] = PostgresClusteringServiceCollectionExtensions.ProviderName
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddLakonaPostgresClustering(configuration));

        Assert.Contains("ConnectionStrings:LakonaClusterPostgres", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedProviderMustBeRegisteredByItsPackage()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Lakona:Cluster:Membership:Provider"] = PostgresClusteringServiceCollectionExtensions.ProviderName
        });
        var services = new ServiceCollection();
        services.AddLakonaGameServer(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IMembershipTable>());

        Assert.Contains("Lakona.Game.Clustering.*", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
