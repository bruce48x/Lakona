using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Features;
using Xunit;

namespace Lakona.Game.Server.Tests;

public sealed class FeatureBuilderTests
{
    [Fact]
    public void All_features_configured_when_no_filter()
    {
        var builder = new FeatureBuilder();
        builder.AddRole(new TestRole("gateway", [new FeatureA(), new FeatureB()]));
        builder.AddRole(new TestRole("room", [new FeatureC()]));

        var features = builder.ResolveFeatures().ToList();

        Assert.Equal(3, features.Count);
        Assert.Contains(features, f => f is FeatureA);
        Assert.Contains(features, f => f is FeatureB);
        Assert.Contains(features, f => f is FeatureC);
    }

    [Fact]
    public void Filter_by_single_role()
    {
        var builder = new FeatureBuilder();
        builder.UseFilter(new FeatureFilter { Roles = ["gateway"] });
        builder.AddRole(new TestRole("gateway", [new FeatureA()]));
        builder.AddRole(new TestRole("room", [new FeatureB()]));

        var features = builder.ResolveFeatures().ToList();

        Assert.Single(features);
        Assert.IsType<FeatureA>(features[0]);
    }

    [Fact]
    public void Filter_by_multiple_roles()
    {
        var builder = new FeatureBuilder();
        builder.UseFilter(new FeatureFilter { Roles = ["gateway", "room"] });
        builder.AddRole(new TestRole("gateway", [new FeatureA()]));
        builder.AddRole(new TestRole("room", [new FeatureB()]));
        builder.AddRole(new TestRole("lobby", [new FeatureC()]));

        var features = builder.ResolveFeatures().ToList();

        Assert.Equal(2, features.Count);
        Assert.IsType<FeatureA>(features[0]);
        Assert.IsType<FeatureB>(features[1]);
    }

    [Fact]
    public void Duplicate_feature_across_roles_is_deduplicated_by_type()
    {
        var builder = new FeatureBuilder();
        builder.AddRole(new TestRole("gateway", [new SharedFeature(), new FeatureA()]));
        builder.AddRole(new TestRole("room", [new SharedFeature(), new FeatureB()]));

        var features = builder.ResolveFeatures().ToList();

        Assert.Equal(3, features.Count);
        Assert.Single(features.OfType<SharedFeature>());
    }

    [Fact]
    public void Missing_role_throws_with_available_roles()
    {
        var builder = new FeatureBuilder();
        builder.UseFilter(new FeatureFilter { Roles = ["nonexistent"] });
        builder.AddRole(new TestRole("gateway", [new FeatureA()]));

        var ex = Assert.Throws<InvalidOperationException>(() => builder.ResolveFeatures().ToList());

        Assert.Contains("nonexistent", ex.Message, StringComparison.Ordinal);
        Assert.Contains("gateway", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Feature_order_preserves_role_array_order()
    {
        var builder = new FeatureBuilder();
        builder.AddRole(new TestRole("main", [new FeatureC(), new FeatureA(), new FeatureB()]));

        var features = builder.ResolveFeatures().ToList();

        Assert.Equal(3, features.Count);
        Assert.IsType<FeatureC>(features[0]);
        Assert.IsType<FeatureA>(features[1]);
        Assert.IsType<FeatureB>(features[2]);
    }

    [Fact]
    public void AddFeatures_extension_invokes_configure_on_filtered_roles()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Features:Roles:0"] = "alpha"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFeatures(config, builder =>
        {
            builder.AddRole(new TestRole("alpha", [new MarkerFeature("Alpha")]));
            builder.AddRole(new TestRole("beta", [new MarkerFeature("Beta")]));
        });

        var provider = services.BuildServiceProvider();
        var configured = provider.GetServices<FeatureConfiguredMarker>()
            .Select(m => m.FeatureName)
            .ToArray();

        Assert.Equal(["Alpha"], configured);
    }

    [Fact]
    public void Add_features_with_no_config_filter_runs_all()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddFeatures(config, builder =>
        {
            builder.AddRole(new TestRole("a", [new MarkerFeatureA()]));
            builder.AddRole(new TestRole("b", [new MarkerFeatureB()]));
        });

        var configured = services.BuildServiceProvider()
            .GetServices<FeatureConfiguredMarker>()
            .Select(m => m.FeatureName)
            .ToArray();

        Assert.Equal(["MarkerA", "MarkerB"], configured);
    }

    [Fact]
    public void Assembly_scanning_discovers_roles()
    {
        var builder = new FeatureBuilder();
        builder.FromAssembly(typeof(DiscoveryRole).Assembly);

        var features = builder.ResolveFeatures().ToList();

        var discovered = features.OfType<DiscoveryFeature>().ToList();
        Assert.Single(discovered);
    }

    [Fact]
    public void FeatureCatalog_enables_all_features_when_config_is_omitted()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Node:Id"] = "dev-1"
            })
            .Build();

        services.AddLakonaGame(configuration, game =>
        {
            game.Feature<MarkerFeatureA>("login");
            game.Feature<MarkerFeatureB>("chat");
        });

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Equal(["login", "chat"], catalog.ActiveNames);
    }

    [Fact]
    public void FeatureCatalog_rejects_unknown_configured_feature()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Node:Id"] = "dev-1",
                ["Lakona.Game:Feature:0"] = "missing"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddLakonaGame(configuration, game => game.Feature<MarkerFeatureA>("login")));

        Assert.Contains("missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeatureCatalog_sorts_after_dependency()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Node:Id"] = "dev-1",
                ["Lakona.Game:Feature:0"] = "battle",
                ["Lakona.Game:Feature:1"] = "settlement"
            })
            .Build();

        services.AddLakonaGame(configuration, game =>
        {
            game.Feature<MarkerFeatureA>("settlement").After("battle");
            game.Feature<MarkerFeatureB>("battle");
        });

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<LakonaGameFeatureCatalog>();

        Assert.Equal(["battle", "settlement"], catalog.ActiveNames);
    }

    [Fact]
    public void FeatureCatalog_rejects_missing_required_feature()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Node:Id"] = "dev-1",
                ["Lakona.Game:Feature:0"] = "settlement"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddLakonaGame(configuration, game =>
            {
                game.Feature<MarkerFeatureA>("settlement").RequiresFeature("battle");
                game.Feature<MarkerFeatureB>("battle");
            }));

        Assert.Contains("settlement", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("battle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddLakonaGame_rejects_feature_missing_required_transport()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Node:Id"] = "game-c",
                ["Lakona.Game:Feature:0"] = "battle"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddLakonaGame(configuration, game =>
            {
                game.Feature<MarkerFeatureA>("battle").RequiresTransport("kcp");
            }));

        Assert.Contains("kcp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddLakonaGame_rejects_feature_missing_cluster()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Node:Id"] = "game-b",
                ["Lakona.Game:Feature:0"] = "chat"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddLakonaGame(configuration, game =>
            {
                game.Feature<MarkerFeatureA>("chat").RequiresCluster();
            }));

        Assert.Contains("Cluster", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FeatureCatalog_rejects_feature_constructor_dependencies()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Lakona.Game:Node:Id"] = "dev-1"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddLakonaGame(configuration, game => game.Feature<ConstructorDependencyFeature>("constructor")));

        Assert.Contains(nameof(ConstructorDependencyFeature), ex.Message, StringComparison.Ordinal);
        Assert.Contains("parameterless", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record FeatureConfiguredMarker(string FeatureName);

    private sealed class TestRole(string name, IFeature[] features) : INodeRole
    {
        public string Name { get; } = name;

        public IFeature[] Features { get; } = features;
    }

    // Distinct feature types for testing deduplication
    private sealed class FeatureA : IFeature
    {
        public void Configure(IServiceCollection services, IConfiguration config) { }
    }

    private sealed class FeatureB : IFeature
    {
        public void Configure(IServiceCollection services, IConfiguration config) { }
    }

    private sealed class FeatureC : IFeature
    {
        public void Configure(IServiceCollection services, IConfiguration config) { }
    }

    private sealed class SharedFeature : IFeature
    {
        public void Configure(IServiceCollection services, IConfiguration config) { }
    }

    private sealed class MarkerFeature(string name) : IFeature
    {
        public void Configure(IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton(new FeatureConfiguredMarker(name));
        }
    }

    // Types for assembly scanning test
    private sealed class MarkerFeatureA : LakonaGameFeature, IFeature
    {
        public void Configure(IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton(new FeatureConfiguredMarker("MarkerA"));
        }

        public override void ConfigureServices(LakonaGameFeatureContext context)
        {
            context.Services.AddSingleton(new FeatureConfiguredMarker("MarkerA"));
        }
    }

    private sealed class MarkerFeatureB : LakonaGameFeature, IFeature
    {
        public void Configure(IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton(new FeatureConfiguredMarker("MarkerB"));
        }

        public override void ConfigureServices(LakonaGameFeatureContext context)
        {
            context.Services.AddSingleton(new FeatureConfiguredMarker("MarkerB"));
        }
    }

    private sealed class DiscoveryRole : INodeRole
    {
        public string Name => "discovery";

        public IFeature[] Features => [new DiscoveryFeature()];
    }

    private sealed class DiscoveryFeature : IFeature
    {
        public void Configure(IServiceCollection services, IConfiguration config)
        {
        }
    }

    private sealed class ConstructorDependencyFeature(string value) : LakonaGameFeature
    {
        public string Value { get; } = value;
    }
}
