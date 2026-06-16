using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Features;
using Xunit;

namespace Lakona.Game.Server.Tests.Features;

public sealed class LakonaGameFeatureDiscoveryTests
{
    [Fact]
    public void NameConventionConvertsFeatureTypesToKebabCase()
    {
        Assert.Equal("database", LakonaGameFeatureName.FromType(typeof(DatabaseFeature)));
        Assert.Equal("state-store", LakonaGameFeatureName.FromType(typeof(StateStoreFeature)));
        Assert.Equal("http-gateway", LakonaGameFeatureName.FromType(typeof(HTTPGatewayFeature)));
    }

    [Fact]
    public void DiscoveryRejectsFeatureNameCollisions()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            LakonaGameFeatureDiscovery.Discover(typeof(DatabaseFeature).Assembly, [
                typeof(DatabaseFeature),
                typeof(DATABASEFeature)
            ]));

        Assert.Contains("database", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OmittedFeatureConfigEnablesDiscoveredAppFeaturesByName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "data-1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLakonaGame(configuration, [typeof(StateStoreFeature), typeof(DatabaseFeature)]);

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Equal(["database", "state-store"], catalog.ActiveNames);
    }

    [Fact]
    public void EmptyFeatureConfigEnablesNoAppFeatures()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Node:Id"] = "gateway-1",
                ["Lakona:Feature"] = ""
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLakonaGame(configuration, [typeof(DatabaseFeature)]);

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Empty(catalog.ActiveNames);
    }

    [Fact]
    public async Task HostedServiceStartsAndStopsFeaturesInConfiguredOrder()
    {
        LifecycleLog.Events.Clear();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona:Feature:0"] = "database",
                ["Lakona:Feature:1"] = "state-store"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGame(configuration, [typeof(DatabaseFeature), typeof(StateStoreFeature)]);

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().OfType<LakonaGameFeatureHostedService>().Single();
        Assert.Single(provider.GetServices<IHostedService>().OfType<LakonaGameClusterRegistrationHostedService>());

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await hosted.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal([
            "database:start",
            "state-store:start",
            "state-store:stop",
            "database:stop"
        ], LifecycleLog.Events);
    }

    private sealed class DatabaseFeature : LakonaGameFeature
    {
        public override ValueTask StartAsync(LakonaGameFeatureContext context, CancellationToken cancellationToken = default)
        {
            LifecycleLog.Events.Add("database:start");
            return default;
        }

        public override ValueTask StopAsync(LakonaGameFeatureContext context, CancellationToken cancellationToken = default)
        {
            LifecycleLog.Events.Add("database:stop");
            return default;
        }
    }

    private sealed class StateStoreFeature : LakonaGameFeature
    {
        public override ValueTask StartAsync(LakonaGameFeatureContext context, CancellationToken cancellationToken = default)
        {
            LifecycleLog.Events.Add("state-store:start");
            return default;
        }

        public override ValueTask StopAsync(LakonaGameFeatureContext context, CancellationToken cancellationToken = default)
        {
            LifecycleLog.Events.Add("state-store:stop");
            return default;
        }
    }

    private sealed class HTTPGatewayFeature : LakonaGameFeature { }

    private sealed class DATABASEFeature : LakonaGameFeature { }

    private static class LifecycleLog
    {
        public static readonly List<string> Events = [];
    }
}
