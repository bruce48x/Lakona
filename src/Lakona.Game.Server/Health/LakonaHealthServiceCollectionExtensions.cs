using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Health;

public static class LakonaHealthServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameHealth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
        return services;
    }
}
