using Agar.Sample.State.Users;
using Lakona.Game.Server;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Gameplay;

namespace Agar.Unity.Tests;

internal static class TestHotfix
{
    public static async Task LoadCurrentAsync(CancellationToken cancellationToken)
    {
        var rootServices = CreateRootServiceProvider();
        _ = await LoadCurrentAsync(rootServices, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<IServiceProvider> LoadCurrentAsync(
        IServiceProvider rootServices,
        CancellationToken cancellationToken)
    {
        var runtime = await LoadCurrentRuntimeAsync(rootServices, cancellationToken).ConfigureAwait(false);
        return runtime.HotfixServices;
    }

    public static async Task<HotfixRuntimeSnapshot> LoadCurrentRuntimeAsync(
        IServiceProvider rootServices,
        CancellationToken cancellationToken)
    {
        // Dispatch-path tests need the current hotfix provider; unload/ALC lifetime is covered outside this helper.
        var hotfixAssemblyPath = FindHotfixAssemblyPath();
        var source = new CurrentDirectoryHotfixAssemblySource(
            Path.GetDirectoryName(hotfixAssemblyPath)!,
            Path.GetFileName(hotfixAssemblyPath));
        var manager = new HotfixManager(source, SharedAssemblyNames(), rootServices: rootServices);

        var reload = await manager.ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (!reload.Succeeded)
        {
            throw new InvalidOperationException(BuildReloadDiagnostics(reload));
        }

        return ((IHotfixRuntimeAccessor)manager).Current;
    }

    public static string FindHotfixAssemblyPath(
        string assemblyFileName = "Server.Hotfix.dll",
        string hotfixProjectDirectoryName = "Hotfix")
    {
        var directCandidate = Path.Combine(AppContext.BaseDirectory, assemblyFileName);
        if (File.Exists(directCandidate))
        {
            return directCandidate;
        }

        var root = FindRepositoryRoot();
        var configuration = GetConfigurationName();
        var candidates = new[]
        {
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", configuration, "net10.0", assemblyFileName),
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", "Debug", "net10.0", assemblyFileName),
            Path.Combine(root, "samples", "Game.Unity.Agar", "Server", hotfixProjectDirectoryName, "bin", "Release", "net10.0", assemblyFileName)
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate {assemblyFileName}. Checked:{Environment.NewLine}{string.Join(Environment.NewLine, candidates.Prepend(directCandidate))}",
            assemblyFileName);
    }

    public static string[] SharedAssemblyNames()
    {
        return
        [
            typeof(ArenaSimulation).Assembly.GetName().Name!,
            typeof(UserActor).Assembly.GetName().Name!,
            typeof(Lakona.Game.Cluster.NodeId).Assembly.GetName().Name!,
            typeof(Lakona.Game.Server.ILakonaGameServer).Assembly.GetName().Name!,
            typeof(Lakona.Game.Server.Hotfix.Abstractions.HotfixFeatureContext).Assembly.GetName().Name!
        ];
    }

    public static ServiceProvider CreateRootServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLakonaGameServer();
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.AddGeneratedActorSelectorTestDependencies();
        return services.BuildServiceProvider();
    }

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples", "Game.Unity.Agar")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }

    public static string BuildReloadDiagnostics(Lakona.Game.Server.Hotfix.Abstractions.HotfixReloadResult reload)
    {
        return string.Join(
            Environment.NewLine,
            new[]
            {
                $"Status: {reload.Status}",
                $"RequestedPath: {reload.RequestedPath}",
                $"ErrorMessage: {reload.ErrorMessage}",
                $"ExceptionType: {reload.ExceptionType}",
                "Diagnostics:",
                string.Join(Environment.NewLine, reload.Diagnostics)
            });
    }

    private static string GetConfigurationName()
    {
#if DEBUG
        return "Debug";
#else
        return "Release";
#endif
    }
}

internal static class AgarTestServiceCollectionExtensions
{
    public static IServiceCollection AddGeneratedActorSelectorTestDependencies(this IServiceCollection services)
    {
        new global::GeneratedHotfixActorRegistration().Register(services);
        services.TryAddSingleton<IRemoteActorInvoker, FailingRemoteActorInvoker>();
        services.TryAddSingleton<IRemoteActorSerializer, FailingRemoteActorSerializer>();
        return services.AddTestHotfixRuntimeAccessor();
    }

    public static IServiceCollection AddTestHotfixRuntimeAccessor(this IServiceCollection services)
    {
        services.TryAddSingleton<IHotfixRuntimeAccessor>(provider =>
            new TestHotfixRuntimeAccessor(provider));
        return services;
    }

    private sealed class TestHotfixRuntimeAccessor : IHotfixRuntimeAccessor
    {
        public TestHotfixRuntimeAccessor(IServiceProvider services)
        {
            Current = new HotfixRuntimeSnapshot(new HotfixServiceInvoker(), services);
        }

        public HotfixRuntimeSnapshot Current { get; }
    }

    private sealed class FailingRemoteActorInvoker : IRemoteActorInvoker
    {
        public ValueTask<RemoteActorInvocationResult> AskAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Remote actor calls are not available in this test service provider.");
        }

        public ValueTask<RemoteActorInvocationResult> TellAsync(
            RemoteActorInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Remote actor calls are not available in this test service provider.");
        }
    }

    private sealed class FailingRemoteActorSerializer : IRemoteActorSerializer
    {
        public ReadOnlyMemory<byte> Serialize<T>(T value)
        {
            throw new InvalidOperationException("Remote actor serialization is not available in this test service provider.");
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> payload)
        {
            throw new InvalidOperationException("Remote actor serialization is not available in this test service provider.");
        }

        public ReadOnlyMemory<byte> Serialize(object? value, Type type)
        {
            throw new InvalidOperationException("Remote actor serialization is not available in this test service provider.");
        }

        public object? Deserialize(ReadOnlyMemory<byte> payload, Type type)
        {
            throw new InvalidOperationException("Remote actor serialization is not available in this test service provider.");
        }
    }
}
