using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.BuildTag;
using Lakona.Game.Server.HotfixAdmin;
using Lakona.Game.Server.Hotfix.Loading;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.Modules;

namespace Lakona.Game.Server.Hosting;

internal sealed record LakonaGameReadinessContext(
    LakonaGameRuntimeOptions RuntimeOptions,
    ClusterOptions ClusterOptions,
    LakonaObservabilityCapabilities ObservabilityCapabilities,
    string HotfixAssemblyPath);

public static class LakonaGameServer
{
    /// <summary>
    /// Builds and runs a Lakona game server using an explicit infrastructure composition root.
    /// </summary>
    /// <param name="args">Command-line arguments forwarded to the .NET host.</param>
    /// <param name="configure">
    /// Configures endpoint implementations, cluster RPC, application services, and RPC bindings.
    /// </param>
    /// <returns>A task that completes with the process exit code after the host stops.</returns>
    public static async Task<int> RunAsync(string[] args, Action<LakonaGameServerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = CreateApplicationBuilder(args);

        var serverBuilder = new LakonaGameServerBuilder(builder);
        configure(serverBuilder);
        serverBuilder.EnsureClusterRpcConfigured();
        serverBuilder.ApplyConfigurationToHostBuilder();

        var runtimeOptions = CreateRuntimeOptions(builder.Configuration);

        // Full startup
        LakonaLoggingConfiguration.Apply(builder.Logging, runtimeOptions.Observability.Logging);
        LakonaBrand.Print();

        builder.Services.AddLakonaGameServer(builder.Configuration);
        builder.Services.AddSingleton(runtimeOptions);
        builder.Services.AddSingleton(DiscoverRpcServiceCatalog());

        serverBuilder.ApplyServiceRegistrationsToHostBuilder();

        var serviceBinder = serverBuilder.GetServiceBinder();
        foreach (var endpoint in runtimeOptions.Endpoints)
        {
            builder.Services.AddSingleton<IRpcServerConfigurator>(_ =>
                new LakonaEndpointRpcServerConfigurator(endpoint, serviceBinder));
        }

        var clusterOptions = runtimeOptions.ToClusterOptions(builder.Configuration);
        builder.Services.AddSingleton(clusterOptions);
        builder.Services.AddLakonaGameClusterEndpoint();

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

        var hotfixAssemblyPath = await ResolveDefaultHotfixAssemblyPathAsync(
            AppContext.BaseDirectory,
            hotfixAdminOptions).ConfigureAwait(false);
        builder.Services.Replace(ServiceDescriptor.Singleton(
            new LakonaHealthReadinessState(hotfixAssemblyPath)));

        ValidateStartupRuntime(
            builder.Services,
            runtimeOptions,
            clusterOptions,
            hotfixAssemblyPath);

        LakonaModuleDiscovery.Configure(
            builder.Services,
            builder.Configuration,
            DiscoverApplicationAssemblies());

        var host = builder.Build();
        await RunHostAsync(host).ConfigureAwait(false);
        return 0;
    }

    private static async Task RunHostAsync(IHost host)
    {
        var modules = host.Services.GetRequiredService<LakonaModuleRuntime>();
        var readiness = host.Services.GetRequiredService<LakonaServerReadinessState>();
        var logger = host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Server.ApplicationModules");
        Exception? failure = null;
        var frameworkStartAttempted = false;

        try
        {
            await modules.StartAsync(CancellationToken.None).ConfigureAwait(false);
            await LoadInitialHotfixAsync(host).ConfigureAwait(false);
            frameworkStartAttempted = true;
            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            readiness.MarkStopping();
            if (frameworkStartAttempted)
            {
                try
                {
                    await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception stopException)
                {
                    logger.LogError(
                        stopException,
                        "Lakona framework cleanup failed after startup or runtime failure.");
                }
            }
        }

        readiness.MarkStopping();
        try
        {
            await modules.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (failure is null)
            {
                failure = exception;
            }
            else
            {
                logger.LogError(
                    exception,
                    "Lakona application module cleanup failed while preserving an earlier server failure.");
            }
        }

        try
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }
        catch (Exception exception)
        {
            if (failure is null)
            {
                failure = exception;
            }
            else
            {
                logger.LogError(
                    exception,
                    "Lakona root provider disposal failed while preserving an earlier server failure.");
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static HostApplicationBuilder CreateApplicationBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.Logging.ClearProviders();
        builder.Services.Configure<ConsoleLifetimeOptions>(options =>
            options.SuppressStatusMessages = true);
        return builder;
    }

    private static async Task<LakonaGameReadinessContext> CreateReadinessContext(
        IHostApplicationBuilder builder,
        Action<LakonaGameServerBuilder> configure,
        string baseDirectory)
    {
        var serverBuilder = new LakonaGameServerBuilder(builder);
        configure(serverBuilder);
        serverBuilder.ApplyToHostBuilder();

        var runtimeOptions = CreateRuntimeOptions(builder.Configuration);
        var clusterOptions = TryBuildClusterOptions(runtimeOptions, builder.Configuration);

        using var provider = builder.Services.BuildServiceProvider();
        var capabilities = LakonaObservabilityCapabilities.FromServices(
            provider.GetServices<ILakonaObservabilityCapability>());
        var hotfixBuildTag = HotfixBuildTag.Get(Assembly.GetEntryAssembly() ?? typeof(LakonaGameServer).Assembly);
        var hotfixAdminOptions = CreateDefaultHotfixAdminOptions(
            builder.Configuration,
            baseDirectory,
            hotfixBuildTag);
        var hotfixAssemblyPath = await ResolveDefaultHotfixAssemblyPathAsync(
            baseDirectory,
            hotfixAdminOptions).ConfigureAwait(false);

        return new LakonaGameReadinessContext(
            runtimeOptions,
            clusterOptions,
            capabilities,
            hotfixAssemblyPath);
    }

    private static ClusterOptions TryBuildClusterOptions(
        LakonaGameRuntimeOptions runtimeOptions,
        IConfiguration configuration)
    {
        return runtimeOptions.ToClusterOptions(configuration);
    }

    internal static LakonaGameRuntimeOptions CreateRuntimeOptionsForTesting(
        IConfiguration configuration)
    {
        return CreateRuntimeOptions(configuration);
    }

    internal static Task<LakonaGameReadinessContext> CreateReadinessContextForTesting(
        string[] args,
        Action<LakonaGameServerBuilder> configure)
    {
        return CreateReadinessContextForTesting(args, configure, AppContext.BaseDirectory);
    }

    internal static Task<LakonaGameReadinessContext> CreateReadinessContextForTesting(
        string[] args,
        Action<LakonaGameServerBuilder> configure,
        string baseDirectory)
    {
        return CreateReadinessContext(CreateApplicationBuilder(args), configure, baseDirectory);
    }

    internal static LakonaGameRuntimeOptions CreateFullStartupRuntimeOptionsForTesting(
        string[] args,
        Action<LakonaGameServerBuilder> configure)
    {
        var builder = CreateApplicationBuilder(args);
        var serverBuilder = new LakonaGameServerBuilder(builder);
        configure(serverBuilder);
        serverBuilder.ApplyConfigurationToHostBuilder();

        return CreateRuntimeOptions(builder.Configuration);
    }

    internal static Task ValidateStartupRuntimeForTesting(
        string[] args,
        Action<LakonaGameServerBuilder> configure)
    {
        return ValidateStartupRuntimeForTesting(args, configure, AppContext.BaseDirectory);
    }

    internal static async Task ValidateStartupRuntimeForTesting(
        string[] args,
        Action<LakonaGameServerBuilder> configure,
        string baseDirectory)
    {
        var builder = CreateApplicationBuilder(args);
        var serverBuilder = new LakonaGameServerBuilder(builder);
        configure(serverBuilder);
        serverBuilder.ApplyConfigurationToHostBuilder();

        var runtimeOptions = CreateRuntimeOptions(builder.Configuration);

        builder.Services.AddLakonaGameServer(builder.Configuration);
        builder.Services.AddSingleton(runtimeOptions);
        serverBuilder.ApplyServiceRegistrationsToHostBuilder();

        var clusterOptions = TryBuildClusterOptions(runtimeOptions, builder.Configuration);
        var hotfixAdminOptions = CreateDefaultHotfixAdminOptions(
            builder.Configuration,
            baseDirectory,
            "test-build");

        ValidateStartupRuntime(
            builder.Services,
            runtimeOptions,
            clusterOptions,
            await ResolveDefaultHotfixAssemblyPathAsync(
                baseDirectory,
                hotfixAdminOptions).ConfigureAwait(false));
    }

    private static void ValidateStartupRuntime(
        IServiceCollection services,
        LakonaGameRuntimeOptions runtimeOptions,
        ClusterOptions? clusterOptions,
        string hotfixAssemblyPath)
    {
        using var provider = services.BuildServiceProvider();
        var capabilities = LakonaObservabilityCapabilities.FromServices(
            provider.GetServices<ILakonaObservabilityCapability>());
        var resolved = LakonaGameReadinessRuntime.ToResolvedRuntimeForValidation(
            runtimeOptions,
            clusterOptions,
            capabilities,
            hotfixAssemblyPath);
        var result = provider
            .GetRequiredService<LakonaGameRuntimeValidator>()
            .Validate(resolved);

        if (result.Succeeded)
        {
            return;
        }

        var logger = provider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Server.StartupValidation");
        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == LakonaGameDiagnosticSeverity.Error)
            {
                logger.LogError(
                    "{Code}: {Message} Repair: {Repair}",
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Repair);
            }
            else
            {
                logger.LogWarning(
                    "{Code}: {Message} Repair: {Repair}",
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Repair);
            }
        }

        var errors = result.Diagnostics
            .Where(static diagnostic => diagnostic.Severity == LakonaGameDiagnosticSeverity.Error)
            .ToArray();
        var firstError = errors[0];
        var noun = errors.Length == 1 ? "error" : "errors";
        throw new InvalidOperationException(
            $"{errors.Length} startup validation {noun}. First error {firstError.Code}: {firstError.Message}");
    }

    private static async Task<string> ResolveDefaultHotfixAssemblyPathAsync(
        string baseDirectory,
        HotfixAdminOptions adminOptions)
    {
        var source = CreateDefaultHotfixAssemblySource(baseDirectory, adminOptions);
        var resolved = await source.ResolveAsync().ConfigureAwait(false);
        return resolved.AssemblyPath;
    }

    private static LakonaGameRuntimeOptions CreateRuntimeOptions(IConfiguration configuration)
    {
        return LakonaGameRuntimeOptions.FromConfiguration(configuration);
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

    private static bool IsTestAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        return name is not null && name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> GetDefaultHotfixHostAssemblyNames()
    {
        return GetDefaultHotfixHostAssemblyNames(
            DiscoverHotfixRequiredServiceContracts(DiscoverApplicationAssemblies()));
    }

    internal static IReadOnlyList<string> GetDefaultHotfixHostAssemblyNames(
        IReadOnlyList<Type> requiredContractTypes)
    {
        ArgumentNullException.ThrowIfNull(requiredContractTypes);
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            typeof(ILakonaGameServer).Assembly.GetName().Name!
        };

        foreach (var contract in requiredContractTypes)
        {
            AddAssemblyName(contract.Assembly);
        }

        if (Assembly.GetEntryAssembly() is { } entryAssembly)
        {
            AddAssemblyName(entryAssembly);
        }

        return names.OrderBy(static name => name, StringComparer.Ordinal).ToArray();

        void AddAssemblyName(Assembly assembly)
        {
            var name = assembly.GetName().Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }
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
                DebugWatcher = "Off",
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
        var source = CreateDefaultHotfixAssemblySource(baseDirectory, adminOptions);
        services.AddLakonaGameHotfix(source, hostAssemblyNames: GetDefaultHotfixHostAssemblyNames());
        if (IsDebugWatcherEnabled(adminOptions))
        {
            services.AddLakonaGameHotfixFileWatcher(options =>
            {
                options.Directory = hotfixDirectory;
                options.Filter = "reload.signal";
            });
        }
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
        return IsDebugWatcherEnabled(adminOptions)
            ? new CurrentDirectoryHotfixAssemblySource(hotfixDirectory, "Server.Hotfix.dll")
            : new VersionPointerHotfixAssemblySource(hotfixDirectory, "current.txt", "Server.Hotfix.dll");
    }

    private static HotfixAdminOptions CreateDefaultHotfixAdminOptions(
        IConfiguration configuration,
        string baseDirectory,
        string buildTag)
    {
        var options = new HotfixAdminOptions();
        var section = configuration.GetSection("Lakona:Hotfix");
        section.Bind(options);
        options.HotfixRoot = Path.Combine(baseDirectory, "hotfix");
        options.BuildTag = buildTag;
        return options;
    }

    private static void CopyHotfixAdminOptions(HotfixAdminOptions source, HotfixAdminOptions target)
    {
        target.HotfixRoot = source.HotfixRoot;
        target.BuildTag = source.BuildTag;
        target.DebugWatcher = source.DebugWatcher;
    }

    private static bool IsDebugWatcherEnabled(HotfixAdminOptions options)
    {
        return options.DebugWatcher.Equals("On", StringComparison.OrdinalIgnoreCase);
    }
}
