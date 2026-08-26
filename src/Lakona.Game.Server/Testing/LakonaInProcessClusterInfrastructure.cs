using System.ComponentModel;
using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lakona.Game.Server.Testing;

/// <summary>
/// The transport boundary used by the separately packaged Lakona in-process
/// cluster test host.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ILakonaInProcessClusterTransport
{
    string Scheme { get; }

    ValueTask<ITransport> ConnectAsync(
        string endpoint,
        CancellationToken cancellationToken = default);

    ValueTask<IRpcConnectionAcceptor> ListenAsync(
        string endpoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the shared framework infrastructure used by one in-process test
/// cluster without exposing Membership or cluster-RPC implementation types.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class LakonaInProcessClusterInfrastructure
{
    private readonly InMemoryMembershipTable membershipTable = new();

    public void ConfigureNode(
        IServiceCollection services,
        ILakonaInProcessClusterTransport transport)
    {
        ConfigureNode(services, transport, [], hotfixAssembly: null);
    }

    public void ConfigureNode(
        IServiceCollection services,
        ILakonaInProcessClusterTransport transport,
        IReadOnlyList<string> roles,
        Assembly? hotfixAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(roles);

        services.RemoveAll<IMembershipTable>();
        services.AddSingleton<IMembershipTable>(membershipTable);
        services.RemoveAll<ClusterBuildTag>();
        services.AddSingleton(new ClusterBuildTag("testcluster"));
        services.RemoveAll<ClusterRpcChannel>();
        services.AddSingleton(new ClusterRpcChannel(
            new ClusterTransportAdapter(transport),
            new MemoryPackRpcSerializer(),
            ClusterProtocol.Identifier));

        if (hotfixAssembly is not null)
        {
            ConfigureHotfix(services, roles, hotfixAssembly);
        }
    }

    private static void ConfigureHotfix(
        IServiceCollection services,
        IReadOnlyList<string> roles,
        Assembly hotfixAssembly)
    {
        var scan = HotfixBehaviorScanner.Scan(hotfixAssembly);
        if (!scan.Succeeded)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, scan.Diagnostics));
        }

        foreach (var registration in hotfixAssembly.GetTypes()
                     .Where(static type => !type.IsAbstract
                         && !type.IsInterface
                         && typeof(IHotfixGeneratedServiceRegistration).IsAssignableFrom(type))
                     .OrderBy(static type => type.FullName, StringComparer.Ordinal)
                     .Select(static type =>
                         (IHotfixGeneratedServiceRegistration)Activator.CreateInstance(type)!))
        {
            registration.Register(services);
        }

        foreach (var descriptor in scan.StartupServices)
        {
            ((ICollection<ServiceDescriptor>)services).Add(descriptor);
        }

        var actorTypes = scan.ActorMethods
            .Select(static method => method.ActorType)
            .Concat(scan.ActorLifecycles.Select(static lifecycle => lifecycle.ActorType))
            .Concat(scan.ActorStartups.Select(static startup => startup.ActorType))
            .Concat(scan.ActorPlacements.Select(static placement => placement.ActorType))
            .Distinct()
            .ToArray();
        var roleCatalog = new NodeRoleCatalog(roles, actorTypes);
        services.RemoveAll<NodeRoleCatalog>();
        services.AddSingleton(roleCatalog);

        const string hotfixVersion = "testcluster";
        var descriptors = actorTypes
            .Where(roleCatalog.IsLocal)
            .Select(static actorType => new ActorHostDescriptor(
                ActorNameResolver.Resolve(actorType),
                "placement:" + actorType.FullName,
                hotfixVersion))
            .ToArray();
        services.RemoveAll<ActorHostDescriptorCatalog>();
        services.AddSingleton(new ActorHostDescriptorCatalog(descriptors));

        var localActorMethods = scan.ActorMethods
            .Where(method => roleCatalog.IsLocal(method.ActorType))
            .ToArray();
        var localActorLifecycles = scan.ActorLifecycles
            .Where(lifecycle => roleCatalog.IsLocal(lifecycle.ActorType))
            .ToArray();
        var localBehaviorTypes = localActorMethods
            .Select(static method => method.BehaviorType)
            .Concat(localActorLifecycles.Select(static lifecycle => lifecycle.BehaviorType))
            .ToHashSet();
        var localMethods = scan.Methods
            .Where(method => localBehaviorTypes.Contains(method.BehaviorType))
            .ToArray();

        services.RemoveAll<IHotfixRuntimeAccessor>();
        services.AddSingleton<IHotfixRuntimeAccessor>(provider =>
            new InProcessHotfixRuntimeAccessor(
                provider,
                hotfixAssembly,
                scan,
                localMethods,
                localActorMethods,
                localActorLifecycles,
                hotfixVersion));
    }

    private sealed class InProcessHotfixRuntimeAccessor : IHotfixRuntimeAccessor, IAsyncDisposable
    {
        private readonly HotfixDispatchTable table;

        internal InProcessHotfixRuntimeAccessor(
            IServiceProvider services,
            Assembly hotfixAssembly,
            HotfixBehaviorScanResult scan,
            IReadOnlyList<HotfixMethodBinding> localMethods,
            IReadOnlyList<HotfixActorMethodDescriptor> localActorMethods,
            IReadOnlyList<HotfixActorLifecycleDescriptor> localActorLifecycles,
            string hotfixVersion)
        {
            table = new HotfixDispatchTable(
                1,
                localMethods,
                scan.Services,
                localActorMethods,
                localActorLifecycles,
                scan.TimerMethods,
                scan.HttpEndpoints);
            table.ValidateMethodShapes();
            table.ValidateModuleActivation(services);
            table.ValidateTypedDispatchDelegates();
            Current = new HotfixRuntimeSnapshot(
                new HotfixServiceInvoker(table),
                services,
                table,
                services,
                hotfixAssembly,
                loadContext: null,
                sourceVersion: hotfixVersion,
                sourcePath: null,
                ownsRuntimeResources: false,
                onRetired: null,
                actorStartups: scan.ActorStartups,
                actorPlacements: scan.ActorPlacements);
        }

        public HotfixRuntimeSnapshot Current { get; }

        public async ValueTask DisposeAsync()
        {
            await Current.RetireAsync().ConfigureAwait(false);
            await table.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class ClusterTransportAdapter(
        ILakonaInProcessClusterTransport transport) : IClusterRpcTransport
    {
        public string Scheme => transport.Scheme;

        public ValueTask<ITransport> ConnectAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) =>
            transport.ConnectAsync(Format(endpoint), cancellationToken);

        public ValueTask<IRpcConnectionAcceptor> ListenAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) =>
            transport.ListenAsync(Format(endpoint), cancellationToken);

        private static string Format(ClusterEndpoint endpoint) =>
            $"{endpoint.Scheme}://{endpoint.Host}:{endpoint.Port}{endpoint.Path}";
    }
}
