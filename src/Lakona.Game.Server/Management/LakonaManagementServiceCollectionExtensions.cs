using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.InternalHttp;
using Lakona.Game.Server.LocalAdmin;
using Lakona.Game.Server.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Management;

public static class LakonaManagementServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaManagement(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp =>
        {
            var runtime = sp.GetRequiredService<LakonaGameRuntimeOptions>();
            var observability = sp.GetRequiredService<LakonaObservabilityOptions>();
            IEnumerable<ILakonaHttpRoute> healthRoutes = runtime.Health.Enabled
                ? sp.GetServices<ILakonaHealthHttpRoute>().Select(route =>
                    (ILakonaHttpRoute)new LakonaHealthHttpRouteAdapter(
                        route,
                        runtime.Health.RequireLoopback))
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LakonaManagementHttpHostedService>());

        return services;
    }
}
