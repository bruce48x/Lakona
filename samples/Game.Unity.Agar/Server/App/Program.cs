using System.Reflection;
using Agar.Sample.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Server.App.Features;
using Server.App.Hosting;
using Server.App.Realtime;
using Server.App.Services;
using Server.App.Generated;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Diagnostics;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var runtimeOptions = LakonaGameRuntimeOptions.FromConfiguration(builder.Configuration);
ConfigureCoreServices(builder.Services);
ConfigureEndpointRpcServers(builder.Services, runtimeOptions);
ConfigureGatewaySampleServices(builder.Services, runtimeOptions);

builder.Services.AddLakonaGame(builder.Configuration, [
    typeof(DatabaseFeature),
    typeof(StateStoreFeature),
    typeof(MatchmakingFeature),
    typeof(LeaderboardFeature),
    typeof(BattleRuntimeFeature)
]);

var hotfixSharedAssemblies = new[] { "Server.App", "Shared", "Lakona.Game.Server" };
LoadHotfixSharedAssemblies(hotfixSharedAssemblies);

var hotfixDirectory = Path.Combine(AppContext.BaseDirectory, "hotfix");
builder.Services.AddLakonaGameHotfix(
    new CurrentDirectoryHotfixAssemblySource(hotfixDirectory, "Server.Hotfix.dll"),
    sharedAssemblyNames: hotfixSharedAssemblies);

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var hotfix = scope.ServiceProvider.GetRequiredService<IHotfixManager>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Server.Hotfix");
    var result = await hotfix.ReloadAsync();
    if (result.Succeeded)
    {
        logger.LogInformation(
            "Initial hotfix load succeeded from {HotfixPath} with {MethodCount} method(s).",
            result.Current.SourcePath,
            result.Current.Methods.Count);
    }
    else
    {
        logger.LogWarning(
            "Initial hotfix load failed for {HotfixPath}: {ErrorMessage}",
            result.RequestedPath,
            result.ErrorMessage);
        foreach (var diagnostic in result.Diagnostics)
        {
            logger.LogWarning("Hotfix diagnostic: {Diagnostic}", diagnostic);
        }
    }
}

await host.RunAsync();

static void LoadHotfixSharedAssemblies(IEnumerable<string> assemblyNames)
{
    foreach (var assemblyName in assemblyNames)
    {
        Assembly.Load(new AssemblyName(assemblyName));
    }
}

static void ConfigureCoreServices(IServiceCollection services)
{
    services.AddLakonaGameServerActors(options =>
    {
        options.MailboxCapacity = 4096;
        options.CallTimeout = TimeSpan.FromSeconds(5);
        options.SlowMessageThreshold = TimeSpan.FromSeconds(1);
    });
    services.AddLakonaGameServer();
    services.AddLakonaGameServerSessionCleanup(options =>
    {
        options.Interval = TimeSpan.FromSeconds(30);
        options.DisconnectedSessionRetention = TimeSpan.FromMinutes(2);
    });
    services.AddMessageRecording();
    services.AddLakonaGameRuntimeValidation();
    services.AddLakonaGameServerGateway();
}

static void ConfigureEndpointRpcServers(IServiceCollection services, LakonaGameRuntimeOptions runtimeOptions)
{
    if (runtimeOptions.Endpoints.Count == 0)
    {
        return;
    }

    services.AddSingleton(LakonaRpcServiceCatalog.FromTypes([
        typeof(LoginRpcServiceBinder),
        typeof(PlayerRpcServiceBinder),
        typeof(BattleRpcServiceBinder)
    ]));

    foreach (var endpoint in runtimeOptions.Endpoints)
    {
        services.AddSingleton<IRpcServerConfigurator>(_ =>
            new LakonaEndpointRpcServerConfigurator(endpoint));
    }
}

static void ConfigureGatewaySampleServices(IServiceCollection services, LakonaGameRuntimeOptions runtimeOptions)
{
    if (!HasRpcService(runtimeOptions, "login") && !HasRpcService(runtimeOptions, "player"))
    {
        return;
    }

    services.AddAgarSampleState();
    services.AddSingleton<SessionDirectory>();
    services.AddSingleton(SelectRealtimeOptions(runtimeOptions));
    services.AddSingleton<GatewayNodeIdentity>();
    services.AddSingleton<MatchmakingMonitor>();
    services.AddSingleton<RoomRuntimeHost>();
    services.AddSingleton<ReliableMatchmakingPublisher>();
    services.AddSingleton<GatewayMatchmakingCoordinator>();
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IRpcSessionLifecycleObserver, PlayerSessionLifecycleObserver>());
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHotfixRequiredServiceContracts, GeneratedHotfixRequiredServiceContracts>());
    services.AddHostedService<DisconnectedSessionCleanupHostedService>();
}

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
