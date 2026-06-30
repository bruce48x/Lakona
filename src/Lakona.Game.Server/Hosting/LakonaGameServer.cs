using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Features;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.BuildTag;
using Lakona.Game.Server.HotfixAdmin;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Observability;

namespace Lakona.Game.Server.Hosting;

internal sealed record LakonaGameReadinessContext(
    LakonaGameRuntimeOptions RuntimeOptions,
    ClusterOptions? ClusterOptions,
    LakonaObservabilityCapabilities ObservabilityCapabilities);

public static class LakonaGameServer
{
    public static Task<int> RunAsync(string[] args)
    {
        return RunAsync(args, _ => { });
    }

    public static async Task<int> RunAsync(string[] args, Action<LakonaGameServerBuilder> configure)
    {
        var builder = CreateApplicationBuilder(args);

        // Health check commands (exit before full startup)
        if (IsReadinessCheckCommand(args))
        {
            var readiness = CreateReadinessContext(builder, configure);
            return LakonaGameReadinessProbe.Run(
                readiness.RuntimeOptions,
                readiness.ClusterOptions,
                args,
                readiness.ObservabilityCapabilities);
        }

        var runtimeOptions = CreateRuntimeOptions(builder.Configuration, builder.Environment.EnvironmentName);

        if (args.Contains("--health-check", StringComparer.Ordinal))
        {
            var clusterOptions = TryBuildClusterOptions(runtimeOptions, builder.Configuration);
            return LakonaGameLivenessProbe.Run(clusterOptions, runtimeOptions);
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
            FeatureServiceCollectionExtensions.RegisterLakonaGameFeatures(
                builder.Services,
                builder.Configuration,
                runtimeOptions,
                catalogBuilder);
        }
        else if (!builder.Services.Any(static service => service.ServiceType == typeof(LakonaGameFeatureCatalog)))
        {
            DiscoverAndRegisterFeatures(
                builder.Services,
                builder.Configuration,
                AppContext.BaseDirectory,
                runtimeOptions);
        }

        // Hotfix
        var hotfixBuildTag = HotfixBuildTag.Get(Assembly.GetEntryAssembly() ?? typeof(LakonaGameServer).Assembly);
        var hotfixAdminOptions = CreateDefaultHotfixAdminOptions(builder.Configuration, AppContext.BaseDirectory, hotfixBuildTag);
        foreach (var providerType in DiscoverHotfixRequiredServiceContractProviders(DiscoverApplicationAssemblies()))
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton(
                typeof(IHotfixRequiredServiceContracts),
                providerType));
        }

        ConfigureDefaultHotfix(builder.Services, AppContext.BaseDirectory, hotfixAdminOptions);
        builder.Services.AddLakonaGameHotfixAdmin(options => CopyHotfixAdminOptions(hotfixAdminOptions, options));

        // Gateway (registers RpcServersHostedService)
        builder.Services.AddLakonaGameServerGateway();

        var host = builder.Build();
        await LoadInitialHotfixAsync(host);
        await host.RunAsync();
        return 0;
    }

    private static HostApplicationBuilder CreateApplicationBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        return builder;
    }

    private static LakonaGameReadinessContext CreateReadinessContext(
        IHostApplicationBuilder builder,
        Action<LakonaGameServerBuilder> configure)
    {
        var serverBuilder = new LakonaGameServerBuilder(builder);
        configure(serverBuilder);
        serverBuilder.ApplyToHostBuilder();

        var runtimeOptions = CreateRuntimeOptions(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        var clusterOptions = TryBuildClusterOptions(runtimeOptions, builder.Configuration);

        using var provider = builder.Services.BuildServiceProvider();
        var capabilities = LakonaObservabilityCapabilities.FromServices(
            provider.GetServices<ILakonaObservabilityCapability>());

        return new LakonaGameReadinessContext(
            runtimeOptions,
            clusterOptions,
            capabilities);
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

    internal static bool IsReadinessCheckCommandForTesting(string[] args)
    {
        return IsReadinessCheckCommand(args);
    }

    internal static LakonaGameRuntimeOptions CreateRuntimeOptionsForTesting(
        IConfiguration configuration,
        string? environmentName)
    {
        return CreateRuntimeOptions(configuration, environmentName);
    }

    internal static LakonaGameReadinessContext CreateReadinessContextForTesting(
        string[] args,
        Action<LakonaGameServerBuilder> configure)
    {
        return CreateReadinessContext(CreateApplicationBuilder(args), configure);
    }

    private static LakonaGameRuntimeOptions CreateRuntimeOptions(
        IConfiguration configuration,
        string? environmentName)
    {
        return LakonaGameRuntimeOptions.FromConfiguration(configuration, environmentName);
    }

    private static bool IsReadinessCheckCommand(string[] args)
    {
        return args.Contains("--readiness-check", StringComparer.Ordinal);
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

    internal static IReadOnlyList<Type> DiscoverHotfixRequiredServiceContractProvidersForTesting(
        IReadOnlyList<Assembly> assemblies)
    {
        return DiscoverHotfixRequiredServiceContractProviders(assemblies);
    }

    internal static IReadOnlyList<Type> DiscoverHotfixRequiredServiceContractsForTesting(
        IReadOnlyList<Assembly> assemblies)
    {
        return DiscoverHotfixRequiredServiceContracts(assemblies);
    }

    private static IReadOnlyList<Type> DiscoverHotfixRequiredServiceContracts(
        IReadOnlyList<Assembly> assemblies)
    {
        var providerTypes = DiscoverHotfixRequiredServiceContractProviders(assemblies);

        var contracts = new List<Type>();
        foreach (var providerType in providerTypes)
        {
            var provider = (IHotfixRequiredServiceContracts)Activator.CreateInstance(providerType)!;
            contracts.AddRange(provider.ServiceContracts);
        }

        return contracts
            .Distinct()
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<Type> DiscoverHotfixRequiredServiceContractProviders(
        IReadOnlyList<Assembly> assemblies)
    {
        return assemblies
            .SelectMany(GetLoadableTypes)
            .Where(static type => typeof(IHotfixRequiredServiceContracts).IsAssignableFrom(type)
                && !type.IsAbstract
                && !type.IsInterface
                && type.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();
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
        DiscoverAndRegisterFeatures(
            services,
            configuration,
            baseDirectory,
            LakonaGameRuntimeOptions.FromConfiguration(configuration));
    }

    internal static void DiscoverStableFeaturesForTesting(
        IServiceCollection services,
        IConfiguration configuration,
        string baseDirectory,
        LakonaGameRuntimeOptions options)
    {
        DiscoverAndRegisterFeatures(services, configuration, baseDirectory, options);
    }

    internal static void DiscoverAndRegisterFeatures(
        IServiceCollection services,
        IConfiguration configuration,
        string baseDirectory,
        LakonaGameRuntimeOptions options)
    {
        _ = baseDirectory;
        var catalogBuilder = new LakonaGameFeatureCatalogBuilder();
        var definitions = DiscoverApplicationAssemblies()
            .Where(static assembly => !IsTestAssembly(assembly))
            .SelectMany(static assembly => LakonaGameFeatureDiscovery.Discover(
                assembly,
                GetLoadableTypes(assembly)
                    .Where(static type => typeof(LakonaGameFeature).IsAssignableFrom(type)
                        && !type.IsAbstract
                        && !type.IsInterface
                        && type.Name.EndsWith("Feature", StringComparison.Ordinal))
                    .ToArray()))
            .OrderBy(static definition => definition.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var definition in definitions)
        {
            catalogBuilder.Feature(definition.Name, definition.ImplementationType);
        }

        FeatureServiceCollectionExtensions.RegisterLakonaGameFeatures(
            services,
            configuration,
            options,
            catalogBuilder);
    }

    private static bool IsTestAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is not null && name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> GetDefaultHotfixSharedAssemblyNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            typeof(ILakonaGameServer).Assembly.GetName().Name!,
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
        var source = CreateDefaultHotfixAssemblySource(baseDirectory, adminOptions);
        services.AddLakonaGameHotfix(source, sharedAssemblyNames: GetDefaultHotfixSharedAssemblyNames());
        services.AddLakonaGameHotfixActorTicks();
    }

    internal static IHotfixAssemblySource CreateDefaultHotfixAssemblySourceForTesting(
        string baseDirectory,
        HotfixAdminOptions adminOptions)
    {
        return CreateDefaultHotfixAssemblySource(baseDirectory, adminOptions);
    }

    private static IHotfixAssemblySource CreateDefaultHotfixAssemblySource(
        string baseDirectory,
        HotfixAdminOptions adminOptions)
    {
        var hotfixDirectory = Path.Combine(baseDirectory, "hotfix");
        return adminOptions.Mode.Equals("production", StringComparison.OrdinalIgnoreCase)
            ? new VersionPointerHotfixAssemblySource(hotfixDirectory, "current.txt", "Server.Hotfix.dll")
            : new CurrentDirectoryHotfixAssemblySource(hotfixDirectory, "Server.Hotfix.dll");
    }

    private static HotfixAdminOptions CreateDefaultHotfixAdminOptions(
        IConfiguration configuration,
        string baseDirectory,
        string buildTag)
    {
        var options = new HotfixAdminOptions();
        var section = configuration.GetSection("Lakona:Hotfix:Admin");
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
