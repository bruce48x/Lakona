using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.InternalHttp;
using Lakona.Game.Server.LocalAdmin;
using Lakona.Game.Server.Observability;

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
        services.TryAddSingleton(sp => new LakonaHttpRouter(
            sp.GetServices<ILakonaHealthHttpRoute>()
                .Select(route => (ILakonaHttpRoute)new LakonaHealthHttpRouteAdapter(
                    route,
                    sp.GetRequiredService<LakonaGameRuntimeOptions>().Health.Http.RequireLoopback))
                .Concat(sp.GetRequiredService<LakonaObservabilityOptions>().LocalAdmin.EffectiveEnabled
                    ? sp.GetServices<ILakonaLocalAdminRoute>().Select(route => (ILakonaHttpRoute)new LakonaLocalAdminHttpRouteAdapter(
                        route,
                        sp.GetRequiredService<LakonaObservabilityOptions>().LocalAdmin.RequireLoopback))
                    : [])));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LakonaHealthHttpHostedService>());

        return services;
    }
}
