using System.ComponentModel;
using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Scanning;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        ConfigureNode(services, transport, roles, hotfixAssembly, new ConfigurationBuilder().Build());
    }

    public void ConfigureNode(
        IServiceCollection services,
        ILakonaInProcessClusterTransport transport,
        IReadOnlyList<string> roles,
        Assembly? hotfixAssembly,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(configuration);

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
            ConfigureHotfix(services, roles, hotfixAssembly, configuration);
        }
    }

    /// <summary>Initializes component-owned root singletons after the node provider is built.</summary>
    public void InitializeNode(IServiceProvider services) =>
        services.GetService<HotfixComponentRegistration>()?.Initialize(services);

    private static void ConfigureHotfix(
        IServiceCollection services,
        IReadOnlyList<string> roles,
        Assembly hotfixAssembly,
        IConfiguration configuration)
    {
        ComponentStateRegistration.Register(services, hotfixAssembly.GetReferencedAssemblies().Select(Assembly.Load)
            .Append(hotfixAssembly));
        var registration = HotfixComponentRegistration.Configure(services, hotfixAssembly, configuration, roles);
        services.AddSingleton(_ => registration);
        var scan = HotfixBehaviorScanner.Scan(hotfixAssembly);
        if (!scan.Succeeded)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, scan.Diagnostics));
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
        var descriptors = HotfixRuntimeComposition.CreateActorHostDescriptors(scan, hotfixVersion)
            .Where(descriptor => roleCatalog.IsLocalActor(descriptor.Actor))
            .Select(static descriptor => new ActorHostDescriptor(
                descriptor.Actor, descriptor.PolicyHash, descriptor.HotfixVersion))
            .ToArray();
        services.RemoveAll<ActorHostDescriptorCatalog>();
        services.AddSingleton(new ActorHostDescriptorCatalog(descriptors));

        services.RemoveAll<IHotfixRuntimeAccessor>();
        services.AddSingleton<IHotfixRuntimeAccessor>(provider =>
            new InProcessHotfixRuntimeAccessor(
                provider,
                hotfixAssembly,
                scan,
                hotfixVersion));
        services.AddSingleton<ILakonaNodeLifecycleParticipant, InProcessComponentParticipant>();
    }

    private sealed class InProcessComponentParticipant(IHotfixRuntimeAccessor accessor) : ILakonaNodeLifecycleParticipant
    {
        public string Name => "in-process-hotfix";
        public LakonaNodeLifecycleStage Stage => LakonaNodeLifecycleStage.Hotfix;
        public Task StartAsync(CancellationToken cancellationToken) =>
            ((InProcessHotfixRuntimeAccessor)accessor).StartAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) =>
            ((InProcessHotfixRuntimeAccessor)accessor).StopAsync(cancellationToken).AsTask();
    }

    private sealed class InProcessHotfixRuntimeAccessor : IHotfixRuntimeAccessor, IAsyncDisposable
    {
        private readonly Lazy<HotfixRuntimeSnapshot> runtime;
        private HotfixDispatchTable? table;
        private IServiceProvider? generationServices;
        private readonly HotfixComponentLifecycle components = new();
        private readonly IServiceProvider root;
        private bool stopping;

        internal InProcessHotfixRuntimeAccessor(
            IServiceProvider services,
            Assembly hotfixAssembly,
            HotfixBehaviorScanResult scan,
            string hotfixVersion)
        {
            root = services;
            // Let the root provider own this accessor before activating modules.
            // If activation fails, host disposal can await partial-generation cleanup.
            runtime = new Lazy<HotfixRuntimeSnapshot>(() =>
            {
                table = HotfixRuntimeComposition.CreateDispatchTable(scan, 1, services);
                table.ValidateMethodShapes();
                generationServices = HotfixRuntimeComposition.BuildProvider(
                    scan.StartupServices, hotfixAssembly, table.ModuleTypes, services);
                table.ValidateModuleActivation(generationServices);
                table.ValidateTypedDispatchDelegates();
                services.GetRequiredService<Sessions.GameSessionLifecycleBindings>()
                    .Publish(table.SessionLifecycleIdentities, () => { });
                return new HotfixRuntimeSnapshot(
                    new HotfixServiceInvoker(table),
                    generationServices,
                    table,
                    generationServices,
                    hotfixAssembly,
                    loadContext: null,
                    sourceVersion: hotfixVersion,
                    sourcePath: null,
                    ownsRuntimeResources: true,
                    onRetired: null,
                    actorStartups: scan.ActorStartups,
                    actorPlacements: scan.ActorPlacements);
            });
        }

        public HotfixRuntimeSnapshot Current => runtime.Value;

        internal async Task StartAsync(CancellationToken token)
        {
            var rollbackTimeout = LakonaNodeLifecycle.ValidateShutdownTimeout(root.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);
            var current = Current;
            try
            {
                using var lease = current.AcquireLease();
                await components.StartAsync(current, root, token).ConfigureAwait(false);
                components.Commit();
            }
            catch (Exception error)
            {
                using var rollback = new CancellationTokenSource(rollbackTimeout);
                var failures = await components.RollbackAsync(current, rollback.Token).ConfigureAwait(false);
                if (failures.Count != 0) error.Data["Lakona.ComponentRollbackFailure"] = new AggregateException(failures);
                throw;
            }
        }

        public ValueTask DisposeAsync() => StopAsync(CancellationToken.None);

        internal async ValueTask StopAsync(CancellationToken token)
        {
            if (stopping) return;
            stopping = true;
            if (runtime.IsValueCreated)
            {
                runtime.Value.Retire(async () =>
                {
                    var failures = await components.StopAsync(runtime.Value, token).ConfigureAwait(false);
                    if (failures.Count != 0) throw new AggregateException("Component shutdown failed.", failures);
                });
                await runtime.Value.RetireAsync().ConfigureAwait(false);
                return;
            }

            var failures = await HotfixResourceCleanup.RunAsync(table, generationServices, null).ConfigureAwait(false);
            if (failures.Count != 0)
                throw new AggregateException("Hotfix composition cleanup failed.", failures);
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
