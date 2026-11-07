using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.ReliablePush;

public static class ReliablePushServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServerReliablePush(
        this IServiceCollection services,
        Action<ReliablePushOptions>? configure = null)
    {
        var options = new ReliablePushOptions();
        configure?.Invoke(options);
        if (options.MaxPendingBytesPerSession <= 0 || options.MaxPendingBytesPerProcess <= 0)
            throw new ArgumentOutOfRangeException(nameof(configure), "Reliable push byte budgets must be positive.");

        services.TryAddSingleton(options);
        services.TryAddSingleton<IReliablePushOutbox, InMemoryReliablePushOutbox>();
        services.TryAddSingleton<IReliablePushAckService, ReliablePushAckService>();
        services.TryAddSingleton<IReliablePushRuntime, ReliablePushRuntime>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IGameSessionLifecycleHandler, ReliablePushSessionLifecycleHandler>());
        return services;
    }

    public static IServiceCollection AddLakonaGameServerReliablePush(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ReliablePushOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddLakonaGameServerReliablePush(options =>
        {
            var section = GetRuntimeSection(configuration).GetSection("ReliablePush");
            if (int.TryParse(section["MaxPendingPerSession"], out var maxPending))
            {
                options.MaxPendingPerSession = maxPending;
            }

            options.MaxPendingBytesPerSession = LakonaConfigurationReader.ReadInt(section,
                nameof(options.MaxPendingBytesPerSession), options.MaxPendingBytesPerSession);
            options.MaxPendingBytesPerProcess = LakonaConfigurationReader.ReadInt(section,
                nameof(options.MaxPendingBytesPerProcess), options.MaxPendingBytesPerProcess);
            configure?.Invoke(options);
        });
    }

    private static IConfigurationSection GetRuntimeSection(IConfiguration configuration)
    {
        return configuration.GetSection("Lakona");
    }
}
