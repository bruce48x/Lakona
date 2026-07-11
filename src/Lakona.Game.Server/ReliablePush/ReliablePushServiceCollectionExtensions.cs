using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.ReliablePush;

public static class ReliablePushServiceCollectionExtensions
{
    public static IServiceCollection AddLakonaGameServerReliablePush(
        this IServiceCollection services,
        Action<ReliablePushOptions>? configure = null)
    {
        var options = new ReliablePushOptions();
        configure?.Invoke(options);

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

            configure?.Invoke(options);
        });
    }

    private static IConfigurationSection GetRuntimeSection(IConfiguration configuration)
    {
        return configuration.GetSection("Lakona");
    }
}
