using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.LocalAdmin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Observability;

public static class LakonaObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameObservability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.TryAddSingleton(sp =>
            sp.GetRequiredService<LakonaGameRuntimeOptions>().Observability);
        services.TryAddSingleton(sp => LakonaObservabilityCapabilities.FromServices(
            sp.GetServices<ILakonaObservabilityCapability>()));
        services.TryAddSingleton<LakonaLocalAdminRouter>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LakonaLocalAdminHostedService>());
        return services;
    }

    public static IServiceCollection AddLakonaGameObservability(
        this IServiceCollection services,
        LakonaObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.RemoveAll<LakonaObservabilityOptions>();
        services.AddSingleton(options);
        return services.AddLakonaGameObservability();
    }
}
