using Agar.Sample.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Server.App.Features;
using Server.App.Generated;
using Server.App.Hosting;
using Server.App.Realtime;
using Server.App.Services;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Diagnostics;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Server;

return await LakonaGameServer.RunAsync(args, server => server
    .AddServices((services, configuration) =>
    {
        // Actor runtime — registered before framework defaults win via TryAddSingleton ordering.
        services.AddLakonaGameServerActors(options =>
        {
            options.CallTimeout = TimeSpan.FromSeconds(5);
            options.SlowMessageThreshold = TimeSpan.FromSeconds(1);
        });

        services.AddLakonaGameServerSessionCleanup(options =>
        {
            options.Interval = TimeSpan.FromSeconds(30);
            options.DisconnectedSessionRetention = TimeSpan.FromMinutes(2);
        });

        services.AddMessageRecording();
        services.AddLakonaGameRuntimeValidation();

        services.AddLakonaGame(configuration, [
            typeof(DatabaseFeature),
            typeof(StateStoreFeature),
            typeof(MatchmakingFeature),
            typeof(LeaderboardFeature),
            typeof(BattleRuntimeFeature)
        ]);

        var runtimeOptions = LakonaGameRuntimeOptions.FromConfiguration(configuration);

        if (HasRpcService(runtimeOptions, "login") || HasRpcService(runtimeOptions, "player"))
        {
            services.AddAgarSampleState();
            services.AddSingleton<SessionDirectory>();
            services.AddSingleton(SelectRealtimeOptions(runtimeOptions));
            services.AddSingleton<GatewayNodeIdentity>();
            services.AddSingleton<MatchmakingMonitor>();
            services.AddSingleton<RoomRuntimeHost>();
            services.AddSingleton<ReliableMatchmakingPublisher>();
            services.AddSingleton<GatewayMatchmakingCoordinator>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcSessionLifecycleObserver, PlayerSessionLifecycleObserver>());
            services.AddHostedService<DisconnectedSessionCleanupHostedService>();
        }
    })
    .UseGeneratedHotfixServices());

static bool HasRpcService(LakonaGameRuntimeOptions runtimeOptions, string serviceName)
{
    return runtimeOptions.Endpoints.Any(endpoint =>
        endpoint.RpcServices.Any(candidate =>
            string.Equals(candidate, serviceName, StringComparison.OrdinalIgnoreCase)));
}

static LakonaGameEndpointOptions SelectRealtimeOptions(LakonaGameRuntimeOptions runtimeOptions)
{
    return runtimeOptions.Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Transport, "kcp", StringComparison.OrdinalIgnoreCase))
        ?? runtimeOptions.Endpoints.FirstOrDefault()
        ?? new LakonaGameEndpointOptions
        {
            Transport = "kcp",
            Serializer = "memorypack",
            Host = "127.0.0.1",
            Port = 20001
        };
}
