using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Health;

public static class LakonaHealthServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameHealth(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(LakonaHealthReadinessState.Defaults());
        services.TryAddSingleton<LakonaGameReadinessEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaHealthHttpRoute, LakonaHealthHttpRoutes.LiveRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaHealthHttpRoute, LakonaHealthHttpRoutes.ReadyRoute>());
        return services;
    }
}
