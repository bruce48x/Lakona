using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Loading;

namespace Lakona.Game.Server.Hosting;

public static class LakonaGameServer
{
    public static Task<int> RunAsync(string[] args)
    {
        return RunAsync(args, _ => { });
    }

    public static async Task<int> RunAsync(string[] args, Action<LakonaGameServerBuilder> configure)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        var serverBuilder = new LakonaGameServerBuilder(builder);
        configure(serverBuilder);
        serverBuilder.ApplyToHostBuilder();

        var runtimeOptions = LakonaGameRuntimeOptions.FromConfiguration(builder.Configuration);

        // Health check commands (exit before full startup)
        if (args.Contains("--lakona-game-check", StringComparer.Ordinal))
        {
            var clusterOptions = runtimeOptions.ToClusterOptions(builder.Configuration, serverBuilder.GetTransport());
            return LakonaGameReadinessProbe.Run(runtimeOptions, clusterOptions, args);
        }

        if (args.Contains("--health-check", StringComparer.Ordinal))
        {
            var clusterOptions = TryBuildClusterOptions(runtimeOptions, builder.Configuration, serverBuilder.GetTransport());
            return LakonaGameLivenessProbe.Run(clusterOptions, runtimeOptions);
        }

        if (args.Contains("--readiness-check", StringComparer.Ordinal))
        {
            var clusterOptions = TryBuildClusterOptions(runtimeOptions, builder.Configuration, serverBuilder.GetTransport());
            return LakonaGameReadinessProbe.Run(runtimeOptions, clusterOptions, args);
        }

        // Full startup
        LakonaBrand.Print();

        builder.Services.AddLakonaGameServer();
        builder.Services.AddSingleton(runtimeOptions);
        builder.Services.AddSingleton(_ => runtimeOptions.ToServerRpcServerOptions(serverBuilder.GetTransport()));

        // Register primary RPC configurator
        builder.Services.AddSingleton<IULinkRpcServerConfigurator>(sp =>
            new LakonaGameRpcConfigurator(
                runtimeOptions.ToServerRpcServerOptions(serverBuilder.GetTransport()),
                serverBuilder.GetSerializerFactory(),
                serverBuilder.GetAcceptorFactory(),
                serverBuilder.GetServiceBinder())
            { Name = "default" });

        // Register additional RPC endpoints
        foreach (var endpoint in serverBuilder.GetAdditionalEndpoints())
        {
            builder.Services.AddSingleton<IULinkRpcServerConfigurator>(sp =>
                new LakonaGameRpcConfigurator(
                    runtimeOptions.ToServerRpcServerOptions(endpoint.Transport),
                    endpoint.SerializerFactory,
                    endpoint.AcceptorFactory,
                    endpoint.ServiceBinder)
                { Name = endpoint.Name });
        }

        // Cluster options (may throw for standalone — wrap gracefully)
        try
        {
            builder.Services.AddSingleton(
                runtimeOptions.ToClusterOptions(builder.Configuration, serverBuilder.GetTransport()));
        }
        catch (InvalidOperationException)
        {
            // Standalone — no cluster config; services that need ClusterOptions handle null
        }

        // Feature registration
        var featureConfig = serverBuilder.GetFeatureConfiguration();
        if (featureConfig is not null)
        {
            var catalogBuilder = new LakonaGameFeatureCatalogBuilder();
            featureConfig(catalogBuilder);
            var catalog = catalogBuilder.Build(runtimeOptions);
            builder.Services.AddSingleton(catalog);
        }
        else
        {
            DiscoverAndRegisterFeatures(builder.Services, builder.Configuration);
        }

        // Hotfix
        var hotfixDirectory = Path.Combine(AppContext.BaseDirectory, "hotfix");
        builder.Services.AddLakonaGameHotfix(
            new CurrentDirectoryHotfixAssemblySource(hotfixDirectory, "Server.Hotfix.dll"),
            sharedAssemblyNames: new[] { "Shared" });

        // Gateway (registers ULinkRpcServersHostedService)
        builder.Services.AddLakonaGameServerGateway();

        var host = builder.Build();
        await LoadInitialHotfixAsync(host);
        await host.RunAsync();
        return 0;
    }

    private static ClusterOptions? TryBuildClusterOptions(
        LakonaGameRuntimeOptions runtimeOptions,
        IConfiguration configuration,
        string transport)
    {
        try
        {
            return runtimeOptions.ToClusterOptions(configuration, transport);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static async Task LoadInitialHotfixAsync(IHost host)
    {
        using var scope = host.Services.CreateScope();
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
            return;
        }

        var diagnostics = result.Diagnostics.Count == 0
            ? ""
            : " Diagnostics: " + string.Join("; ", result.Diagnostics);
        var message = $"Initial hotfix load failed for '{result.RequestedPath}': {result.ErrorMessage}{diagnostics}";
        logger.LogError("{Message}", message);
        throw new InvalidOperationException(message);
    }

    private static void DiscoverAndRegisterFeatures(
        IServiceCollection services,
        IConfiguration configuration)
    {
        var featureBuilder = new FeatureBuilder();

        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            featureBuilder.FromAssembly(entryAssembly);
        }

        // Referenced assemblies with project prefix
        var entryName = entryAssembly?.GetName().Name ?? "";
        foreach (var referencedAssembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = referencedAssembly.GetName().Name;
            if (name is not null
                && name.StartsWith(entryName, StringComparison.OrdinalIgnoreCase)
                && name != entryName) // don't double-scan entry assembly
            {
                featureBuilder.FromAssembly(referencedAssembly);
            }
        }

        // Hotfix assemblies
        var hotfixDir = Path.Combine(AppContext.BaseDirectory, "hotfix");
        if (Directory.Exists(hotfixDir))
        {
            foreach (var dll in Directory.GetFiles(hotfixDir, "*.dll"))
            {
                try
                {
                    var hotfixAssembly = Assembly.LoadFrom(dll);
                    featureBuilder.FromAssembly(hotfixAssembly);
                }
                catch
                {
                    // Skip unloadable assemblies
                }
            }
        }

        var features = featureBuilder.ResolveFeatures()
            .OrderBy(f => f.GetType().Assembly.GetName().Name)
            .ThenBy(f => f.GetType().FullName)
            .ToArray();

        foreach (var feature in features)
        {
            feature.Configure(services, configuration);
            services.AddSingleton(feature.GetType(), feature);
        }
    }
}
