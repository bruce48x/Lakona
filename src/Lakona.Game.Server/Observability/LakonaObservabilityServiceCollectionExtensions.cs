using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.LocalAdmin;
using Lakona.Game.Server.Observability.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Observability;

public static class LakonaObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameObservability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.TryAddSingleton(sp =>
            sp.GetRequiredService<LakonaGameRuntimeOptions>().Observability);
        services.AddLakonaDiagnosticsEventBuffer();
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

    private static void AddLakonaDiagnosticsEventBuffer(this IServiceCollection services)
    {
        var options = ResolveObservabilityOptions(services).Diagnostics.EventBuffer;

        if (!options.Enabled)
        {
            return;
        }

        services.TryAddSingleton<IDiagnosticsEventSink>(
            _ => new BoundedDiagnosticsEventBuffer(options.Capacity, options.MinimumLevel));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DiagnosticsEventLoggerProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActorDiagnosticsObserver, ActorDiagnosticsEventBridge>());
    }

    private static LakonaObservabilityOptions ResolveObservabilityOptions(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();

        try
        {
            return provider.GetService<LakonaObservabilityOptions>()
                ?? LakonaObservabilityOptions.Defaults(Guardrails.LakonaGameRuntimeProfile.Development);
        }
        catch (InvalidOperationException)
        {
            return LakonaObservabilityOptions.Defaults(Guardrails.LakonaGameRuntimeProfile.Development);
        }
    }
}
