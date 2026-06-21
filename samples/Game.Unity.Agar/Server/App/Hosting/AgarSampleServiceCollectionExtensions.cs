using Agar.Sample.State;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.App.Hotfix;
using Server.App.Realtime;
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

        var controlEndpoint = options.Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Transport, "websocket", StringComparison.OrdinalIgnoreCase));
        if (controlEndpoint is not null)
        {
            services.TryAddSingleton(_ => new GatewayNodeIdentity(
                GatewayEndpointDescriptorFactory.FromConfiguredEndpoint(configuration, controlEndpoint)));
            services.TryAddSingleton<RoomRuntimeHost>();
            services.TryAddSingleton<ReliableMatchmakingPublisher>();
            services.TryAddSingleton<AgarHotfixRuntimeEvents>();
            services.AddLakonaGameSessionHotfixLifecycle();
        }

        return services;
    }
}
