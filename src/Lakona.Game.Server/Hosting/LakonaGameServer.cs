using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.BuildTag;
using Lakona.Game.Server.HotfixAdmin;
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

        var runtimeOptions = LakonaGameRuntimeOptions.FromConfiguration(builder.Configuration);

        // Health check commands (exit before full startup)
        if (args.Contains("--lakona-game-check", StringComparer.Ordinal))
        {
            var clusterOptions = runtimeOptions.ToClusterOptions(builder.Configuration);
            return LakonaGameReadinessProbe.Run(runtimeOptions, clusterOptions, args);
        }

        if (args.Contains("--health-check", StringComparer.Ordinal))
        {
            var clusterOptions = TryBuildClusterOptions(runtimeOptions, builder.Configuration);
            return LakonaGameLivenessProbe.Run(clusterOptions, runtimeOptions);
        }

        if (args.Contains("--readiness-check", StringComparer.Ordinal))
        {
            var clusterOptions = TryBuildClusterOptions(runtimeOptions, builder.Configuration);
            return LakonaGameReadinessProbe.Run(runtimeOptions, clusterOptions, args);
        }

        // Full startup
        LakonaBrand.Print();

        builder.Services.AddLakonaGameServer(builder.Configuration);
        builder.Services.AddSingleton(runtimeOptions);
        builder.Services.AddSingleton(DiscoverRpcServiceCatalog());

        var serverBuilder = new LakonaGameServerBuilder(builder);
        configure(serverBuilder);
        serverBuilder.ApplyToHostBuilder();

        var serviceBinder = serverBuilder.GetServiceBinder();
        foreach (var endpoint in runtimeOptions.Endpoints)
        {
            builder.Services.AddSingleton<IRpcServerConfigurator>(_ =>
                new LakonaEndpointRpcServerConfigurator(endpoint, serviceBinder));
        }

        // Cluster options (may throw for standalone — wrap gracefully)
        try
        {
            builder.Services.AddSingleton(
                runtimeOptions.ToClusterOptions(builder.Configuration));
            builder.Services.AddLakonaGameClusterEndpoint();
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
        else if (!builder.Services.Any(static service => service.ServiceType == typeof(LakonaGameFeatureCatalog)))
        {
            DiscoverAndRegisterFeatures(builder.Services, builder.Configuration, AppContext.BaseDirectory);
        }

        // Hotfix
        var hotfixBuildTag = HotfixBuildTag.Get(Assembly.GetEntryAssembly() ?? typeof(LakonaGameServer).Assembly);
        var hotfixAdminOptions = CreateDefaultHotfixAdminOptions(builder.Configuration, AppContext.BaseDirectory, hotfixBuildTag);
        ConfigureDefaultHotfix(builder.Services, AppContext.BaseDirectory, hotfixAdminOptions);
        builder.Services.AddLakonaGameHotfixAdmin(options => CopyHotfixAdminOptions(hotfixAdminOptions, options));

        // Gateway (registers RpcServersHostedService)
        builder.Services.AddLakonaGameServerGateway();

        var host = builder.Build();
        await LoadInitialHotfixAsync(host);
        await host.RunAsync();
        return 0;
    }

    private static ClusterOptions? TryBuildClusterOptions(
        LakonaGameRuntimeOptions runtimeOptions,
        IConfiguration configuration)
    {
        try
        {
            return runtimeOptions.ToClusterOptions(configuration);
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

    internal static LakonaRpcServiceCatalog DiscoverRpcServiceCatalogForTesting(IReadOnlyList<Type> binderTypes)
    {
        return LakonaRpcServiceCatalog.FromTypes(binderTypes);
    }

    internal static LakonaRpcServiceCatalog DiscoverRpcServiceCatalog()
    {
        var binderTypes = DiscoverApplicationAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(static type => typeof(LakonaRpcServiceBinder).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.IsInterface)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        return LakonaRpcServiceCatalog.FromTypes(binderTypes);
    }

    private static IReadOnlyList<Assembly> DiscoverApplicationAssemblies()
    {
        var assemblies = new List<Assembly>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entryAssembly = Assembly.GetEntryAssembly();

        if (entryAssembly is not null)
        {
            AddAssembly(entryAssembly);
        }

        var entryName = entryAssembly?.GetName().Name ?? "";
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (assembly == typeof(LakonaGameServer).Assembly
                || (!string.IsNullOrWhiteSpace(entryName)
                    && name.StartsWith(entryName, StringComparison.OrdinalIgnoreCase)))
            {
                AddAssembly(assembly);
            }
        }

        return assemblies;

        void AddAssembly(Assembly assembly)
        {
            var name = assembly.GetName().Name;
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
            {
                assemblies.Add(assembly);
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type is not null)!;
        }
    }

    internal static void DiscoverStableFeaturesForTesting(
        IServiceCollection services,
        IConfiguration configuration,
        string baseDirectory)
    {
        DiscoverAndRegisterFeatures(services, configuration, baseDirectory);
    }

    internal static void DiscoverAndRegisterFeatures(
        IServiceCollection services,
        IConfiguration configuration,
        string baseDirectory)
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

        // Do not scan hotfix/*.dll here. Hotfix assemblies are loaded only by
        // HotfixManager into a collectible AssemblyLoadContext.

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

    internal static IReadOnlyList<string> GetDefaultHotfixSharedAssemblyNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "Shared",
            "Server.App",
            "State.Contracts"
        };

        var entryName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(entryName))
        {
            names.Add(entryName);
        }

        return names.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    }

    internal static void ConfigureDefaultHotfixForTesting(
        IServiceCollection services,
        string baseDirectory,
        string buildTag)
    {
        ConfigureDefaultHotfix(
            services,
            baseDirectory,
            new HotfixAdminOptions
            {
                Enabled = true,
                Mode = "production",
                HotfixRoot = Path.Combine(baseDirectory, "hotfix"),
                BuildTag = buildTag
            });
    }

    private static void ConfigureDefaultHotfix(
        IServiceCollection services,
        string baseDirectory,
        HotfixAdminOptions adminOptions)
    {
        var hotfixDirectory = Path.Combine(baseDirectory, "hotfix");
        IHotfixAssemblySource source = adminOptions.Enabled && adminOptions.Mode.Equals("production", StringComparison.OrdinalIgnoreCase)
            ? new VersionPointerHotfixAssemblySource(hotfixDirectory, "current.txt", "Server.Hotfix.dll")
            : new CurrentDirectoryHotfixAssemblySource(hotfixDirectory, "Server.Hotfix.dll");
        services.AddLakonaGameHotfix(source, sharedAssemblyNames: GetDefaultHotfixSharedAssemblyNames());
    }

    private static HotfixAdminOptions CreateDefaultHotfixAdminOptions(
        IConfiguration configuration,
        string baseDirectory,
        string buildTag)
    {
        var options = new HotfixAdminOptions();
        var section = configuration.GetSection("Lakona:Hotfix:Admin");
        if (!section.Exists())
        {
            section = configuration.GetSection("Lakona.Game:Hotfix:Admin");
        }

        section.Bind(options);
        options.HotfixRoot = Path.Combine(baseDirectory, "hotfix");
        options.BuildTag = buildTag;
        return options;
    }

    private static void CopyHotfixAdminOptions(HotfixAdminOptions source, HotfixAdminOptions target)
    {
        target.Enabled = source.Enabled;
        target.Host = source.Host;
        target.Port = source.Port;
        target.HotfixRoot = source.HotfixRoot;
        target.BuildTag = source.BuildTag;
        target.Mode = source.Mode;
    }
}
