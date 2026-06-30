using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hotfix.Timers;

internal static class LakonaTimerServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaTimers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<LakonaTimerOptions>();
        services.TryAddSingleton<ILakonaTimerSchedulerObserver>(NullLakonaTimerSchedulerObserver.Instance);
        services.TryAddSingleton(provider => new LakonaTimerScheduler(
            provider.GetService<IHotfixRuntimeAccessor>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<LakonaTimerOptions>(),
            provider.GetService<ILakonaTimerSchedulerObserver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LakonaTimerScheduler>>()));
        services.TryAddSingleton<ILakonaTimerBackend>(provider =>
            new LakonaTimerBackend(provider.GetRequiredService<LakonaTimerScheduler>()));
        if (!services.Any(static descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(LakonaTimerScheduler)))
        {
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<LakonaTimerScheduler>());
        }

        return services;
    }
}
