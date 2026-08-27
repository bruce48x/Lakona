using Lakona.Game.Cluster.Membership;
using Lakona.Game.Clustering.Redis;
using Lakona.Game.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lakona.Game.Cluster.Tests;

public sealed class RedisClusteringRegistrationTests
{
    [Fact]
    public async Task SelectedProviderReplacesTheBuiltInMemoryTable()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Lakona:Cluster:Membership:Provider"] = RedisClusteringServiceCollectionExtensions.ProviderName,
            ["Lakona:Cluster:Membership:ConnectionStringName"] = "LakonaClusterRedis",
            ["ConnectionStrings:LakonaClusterRedis"] = "127.0.0.1:6379,abortConnect=false"
        });
        var services = new ServiceCollection();
        services.AddLakonaGameServer(configuration);
        services.AddLakonaRedisClustering(configuration);

        await using var provider = services.BuildServiceProvider();
        Assert.IsType<RedisMembershipTable>(provider.GetRequiredService<IMembershipTable>());
    }

    [Fact]
    public void UnselectedProviderLeavesTheBuiltInMemoryTable()
    {
        var configuration = Configuration([]);
        var services = new ServiceCollection();
        services.AddLakonaGameServer(configuration);
        services.AddLakonaRedisClustering(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<InMemoryMembershipTable>(provider.GetRequiredService<IMembershipTable>());
    }

    [Fact]
    public void SelectedProviderRequiresItsNamedConnection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Lakona:Cluster:Membership:Provider"] = RedisClusteringServiceCollectionExtensions.ProviderName,
            ["Lakona:Cluster:Membership:ConnectionStringName"] = "LakonaClusterRedis"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddLakonaRedisClustering(configuration));

        Assert.Contains("ConnectionStrings:LakonaClusterRedis", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitKeyRequiresAClusterHashTag()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddLakonaRedisClustering("127.0.0.1:6379", "lakona-membership"));

        Assert.Contains("hash tag", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
