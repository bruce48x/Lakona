using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Features;

public static class FeatureServiceCollectionExtensions
{
    public static IServiceCollection AddFeatures(
        this IServiceCollection services,
        IConfiguration config,
        Action<FeatureBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new FeatureBuilder();

        var filter = config.GetSection("Lakona.Game:Features").Get<FeatureFilter>();
        if (filter is not null)
        {
            builder.UseFilter(filter);
        }

        configure(builder);

        foreach (var feature in builder.ResolveFeatures())
        {
            feature.Configure(services, config);
        }

        return services;
    }

    public static IServiceCollection AddLakonaGame(
        this IServiceCollection services,
        IConfiguration config,
        Action<LakonaGameFeatureCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(configure);

        var options = LakonaGameRuntimeOptions.FromConfiguration(config);
        var builder = new LakonaGameFeatureCatalogBuilder();
        configure(builder);

        RegisterLakonaGameFeatures(services, config, options, builder);

        return services;
    }

    public static IServiceCollection AddLakonaGame(
        this IServiceCollection services,
        IConfiguration config,
        IReadOnlyList<Type>? featureTypes = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var options = LakonaGameRuntimeOptions.FromConfiguration(config);
        var builder = new LakonaGameFeatureCatalogBuilder();
        var definitions = featureTypes is null
            ? LakonaGameFeatureDiscovery.DiscoverLoadedAssemblies()
            : LakonaGameFeatureDiscovery.Discover(typeof(LakonaGameFeature).Assembly, featureTypes);

        foreach (var definition in definitions.OrderBy(definition => definition.Name, StringComparer.Ordinal))
        {
            builder.Feature(definition.Name, definition.ImplementationType);
        }

        RegisterLakonaGameFeatures(services, config, options, builder);

        return services;
    }

    private static void RegisterLakonaGameFeatures(
        IServiceCollection services,
        IConfiguration config,
        LakonaGameRuntimeOptions options,
        LakonaGameFeatureCatalogBuilder builder)
    {
        var builtCatalog = builder.Build(options);
        ValidateFeatureDependencies(builtCatalog.ActiveDefinitions, options);
        var endpointCatalog = new LakonaGameEndpointCatalog(options.Endpoints);
        var context = new LakonaGameFeatureContext(services, config, endpointCatalog);
        var features = builtCatalog.ActiveDefinitions
            .Select(CreateFeature)
            .ToArray();
        var catalog = new LakonaGameFeatureCatalog(builtCatalog.ActiveDefinitions, features);

        services.AddSingleton(options);
        services.AddSingleton(catalog);
        services.AddSingleton(endpointCatalog);
        services.AddSingleton(context);
        services.AddSingleton<IHostedService, LakonaGameFeatureHostedService>();
        services.AddSingleton<IHostedService, LakonaGameClusterRegistrationHostedService>();

        foreach (var feature in features)
        {
            feature.ConfigureServices(context);
        }
    }

    private static void ValidateFeatureDependencies(
        IReadOnlyList<LakonaGameFeatureDefinition> activeDefinitions,
        LakonaGameRuntimeOptions options)
    {
        var transports = options.Endpoints
            .Select(endpoint => endpoint.Transport)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in activeDefinitions)
        {
            foreach (var transport in definition.RequiredTransports)
            {
                if (!transports.Contains(transport))
                {
                    throw new InvalidOperationException(
                        $"Lakona.Game feature '{definition.Name}' requires transport '{transport}', but that transport is not configured.");
                }
            }

            if (definition.IsClusterRequired && options.Cluster is null)
            {
                throw new InvalidOperationException(
                    $"Lakona.Game feature '{definition.Name}' requires Cluster configuration.");
            }
        }
    }

    private static LakonaGameFeature CreateFeature(LakonaGameFeatureDefinition definition)
    {
        try
        {
            return (LakonaGameFeature?)Activator.CreateInstance(definition.ImplementationType)
                ?? throw new InvalidOperationException(
                    $"Lakona.Game feature '{definition.Name}' ({definition.ImplementationType.FullName}) could not be created.");
        }
        catch (MissingMethodException ex)
        {
            throw new InvalidOperationException(
                $"Lakona.Game feature '{definition.Name}' ({definition.ImplementationType.FullName}) must have a public parameterless constructor. Pass dependencies through LakonaGameFeatureContext instead.",
                ex);
        }
    }
}
