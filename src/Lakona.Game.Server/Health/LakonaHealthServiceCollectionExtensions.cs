using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Health;

public static class LakonaHealthServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameHealth(this IServiceCollection services)
    {
        return AddLakonaGameHealth(services, Configuration.LakonaHealthOptions.Defaults());
    }

    public static IServiceCollection AddLakonaGameHealth(
        this IServiceCollection services,
        Configuration.LakonaHealthOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.TryAddSingleton(LakonaHealthReadinessState.Defaults());
        services.TryAddSingleton<LakonaServerReadinessState>();
        services.TryAddSingleton(provider => new LakonaGameReadinessEvaluator(
            provider.GetRequiredService<Configuration.LakonaGameRuntimeOptions>(),
            provider.GetRequiredService<Configuration.ClusterOptions>(),
            provider.GetRequiredService<Observability.LakonaObservabilityCapabilities>(),
            provider.GetRequiredService<LakonaHealthReadinessState>(),
            provider.GetRequiredService<Guardrails.LakonaGameRuntimeValidator>(),
            provider.GetRequiredService<LakonaServerReadinessState>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaHealthHttpRoute, LakonaHealthHttpRoutes.LiveRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaHealthHttpRoute, LakonaHealthHttpRoutes.ReadyRoute>());
        if (options.ClusterDiagnosticsEnabled)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaHealthHttpRoute, LakonaHealthHttpRoutes.ClusterRoute>());
        }
        return services;
    }
}
