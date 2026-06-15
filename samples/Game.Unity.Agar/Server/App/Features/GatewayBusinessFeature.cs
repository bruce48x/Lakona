using Agar.Sample.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.App.Hosting;
using Server.App.Realtime;
using Server.App.Generated;
using Server.App.Services;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Server;

namespace Server.App.Features;

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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcSessionLifecycleObserver, PlayerSessionLifecycleObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHotfixRequiredServiceContracts, GeneratedHotfixRequiredServiceContracts>());
        services.AddHostedService<MatchmakingHostedService>();
        services.AddHostedService<DisconnectedSessionCleanupHostedService>();
    }
}
