using Agar.Sample.State;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.App.Services;

namespace Server.App.Hosting;

public static class AgarSampleServiceCollectionExtensions
{
    public static IServiceCollection AddAgarSampleServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = LakonaGameRuntimeOptions.FromConfiguration(configuration);
        services.AddAgarSampleActors();
        services.TryAddSingleton<SessionDirectory>();
        services.TryAddSingleton<BattleRuntimeGatewayResolver>();
        services.TryAddSingleton<RoomCallbackPublisher>();

        if (IsFeatureActive(options, "database"))
        {
            services.AddAgarDatabaseInfrastructure(configuration);
        }

        var controlEndpoint = options.Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Transport, "websocket", StringComparison.OrdinalIgnoreCase));
        var battleEndpoint = options.Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Transport, "kcp", StringComparison.OrdinalIgnoreCase));
        var identityEndpoint = battleEndpoint ?? controlEndpoint;
        if (identityEndpoint is not null)
        {
            services.TryAddSingleton(_ => new GatewayNodeIdentity(
                GatewayEndpointDescriptorFactory.FromConfiguredEndpoint(configuration, identityEndpoint)));
        }

        if (controlEndpoint is not null)
        {
            services.TryAddSingleton<ReliableMatchmakingPublisher>();
            services.AddLakonaGameSessionHotfixLifecycle();
        }

        return services;
    }

    private static bool IsFeatureActive(LakonaGameRuntimeOptions options, string feature)
    {
        return options.Feature is null ||
            options.Feature.Contains(feature, StringComparer.OrdinalIgnoreCase);
    }
}
