using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        services.TryAddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<LakonaGameRuntimeOptions>();
            var observability = sp.GetRequiredService<LakonaObservabilityOptions>();
            IEnumerable<ILakonaHttpRoute> healthRoutes = runtime.Health.Http.Enabled
                ? sp.GetServices<ILakonaHealthHttpRoute>().Select(route =>
                    (ILakonaHttpRoute)new LakonaHealthHttpRouteAdapter(
                        route,
                        runtime.Health.Http.RequireLoopback))
                : [];
            IEnumerable<ILakonaHttpRoute> localAdminRoutes = observability.LocalAdmin.EffectiveEnabled
                ? sp.GetServices<ILakonaLocalAdminRoute>().Select(route =>
                    (ILakonaHttpRoute)new LakonaLocalAdminHttpRouteAdapter(
                        route,
                        observability.LocalAdmin.RequireLoopback))
                : [];

            return new LakonaHttpRouter(
                healthRoutes.Concat(localAdminRoutes),
                sp.GetRequiredService<ILogger<LakonaHttpRouter>>());
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LakonaHealthHttpHostedService>());

        return services;
    }
}
