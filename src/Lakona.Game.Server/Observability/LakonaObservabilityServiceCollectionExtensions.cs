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
        services.TryAddSingleton<LakonaDiagnosticsSnapshotService>();
        services.AddLakonaDiagnosticsSnapshotProviders();
        services.AddLakonaDiagnosticsLocalAdminRoutes();
        services.TryAddSingleton<LakonaLocalAdminRouter>();
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
        services.TryAddSingleton<IDiagnosticsEventSink>(
            sp =>
            {
                var options = sp.GetRequiredService<LakonaObservabilityOptions>().Diagnostics.EventBuffer;
                return options.Enabled
                    ? new BoundedDiagnosticsEventBuffer(options.Capacity, options.MinimumLevel)
                    : DisabledDiagnosticsEventSink.Instance;
            });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DiagnosticsEventLoggerProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActorDiagnosticsObserver, ActorDiagnosticsEventBridge>());
    }

    private static void AddLakonaDiagnosticsSnapshotProviders(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaDiagnosticsSnapshotProvider, ProcessDiagnosticsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaDiagnosticsSnapshotProvider, ActorDiagnosticsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaDiagnosticsSnapshotProvider, SessionDiagnosticsProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaDiagnosticsSnapshotProvider, HotfixDiagnosticsProvider>());
    }

    private static void AddLakonaDiagnosticsLocalAdminRoutes(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, DiagnosticsLocalAdminRoutes.SummaryRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, DiagnosticsLocalAdminRoutes.EventsRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, DiagnosticsLocalAdminRoutes.NetstatRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, DiagnosticsLocalAdminRoutes.ActorsRoute>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILakonaLocalAdminRoute, DiagnosticsLocalAdminRoutes.SessionsRoute>());
    }
}
