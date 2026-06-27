using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
            if (bool.TryParse(section["Enabled"], out var enabled))
            {
                options.Enabled = enabled;
            }

            if (int.TryParse(section["MaxPendingPerOwner"], out var maxPending))
            {
                options.MaxPendingPerOwner = maxPending;
            }

            configure?.Invoke(options);
        });
    }

    private static IConfigurationSection GetRuntimeSection(IConfiguration configuration)
    {
        return configuration.GetSection("Lakona");
    }
}
