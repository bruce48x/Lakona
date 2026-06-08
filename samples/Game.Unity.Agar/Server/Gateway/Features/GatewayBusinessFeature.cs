using Agar.Sample.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Gateway.Hosting;
using Gateway.Realtime;
using Gateway.Services;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hosting;

namespace Gateway.Features;

public sealed class GatewayBusinessFeature : LakonaGameFeature
{
    public override void ConfigureServices(LakonaGameFeatureContext context)
    {
        ConfigureServices(context.Services, context.Configuration);
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAgarSampleState();
        services.AddSingleton<SessionDirectory>();

        var runtimeOptions = LakonaGameRuntimeOptions.FromConfiguration(configuration);
        var kcpOptions = runtimeOptions.ToServerRpcServerOptions("kcp");
        services.AddSingleton(kcpOptions);
        services.AddSingleton<IRpcServerConfigurator>(_ =>
            new DefaultControlPlaneRpcServerConfigurator(
                runtimeOptions.ToServerRpcServerOptions("websocket")));
        services.AddSingleton<IRpcServerConfigurator>(_ =>
            new DefaultRealtimeRpcServerConfigurator(kcpOptions));

        services.AddSingleton<GatewayNodeIdentity>();
        services.AddSingleton<MatchmakingMonitor>();
        services.AddSingleton<RoomRuntimeHost>();
        services.AddSingleton<ReliableMatchmakingPublisher>();
        services.AddSingleton<GatewayMatchmakingCoordinator>();
        services.AddHostedService<MatchmakingHostedService>();
        services.AddHostedService<DisconnectedSessionCleanupHostedService>();
    }
}
