using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Membership;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Timers;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Lakona.Game.Server;

/// <summary>
/// Registers Lakona game-server framework services in dependency injection.
/// </summary>
public static class LakonaGameServerServiceCollectionExtensions
{
    /// <summary>
    /// Adds game-server services using default framework options.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <remarks>
    /// This registers actors, sessions, reliable push, hotfix lifecycle support,
    /// timers, guardrails, and the default <see cref="ILakonaGameServer"/>.
    /// Hosts that use <c>LakonaGameServer.RunAsync</c> normally do not need to call
    /// this method directly. Direct hosts receive the framework-owned TCP +
    /// MemoryPack cluster channel automatically; process-local Actor-only hosts
    /// should use <c>AddLakonaGameServerActors</c> instead.
    /// </remarks>
    public static IServiceCollection AddLakonaGameServer(this IServiceCollection services)
    {
        services.TryAddSingleton(new LakonaGameRuntimeOptions());
        return services.AddLakonaGameServer(new LakonaGameHostingOptions());
    }

    /// <summary>
    /// Adds game-server services using options bound from configuration.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The configuration source for Lakona game-server options.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddLakonaGameServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(configuration);
        services.TryAddSingleton(LakonaGameRuntimeOptions.FromConfiguration(configuration));
        return services.AddLakonaGameServer(
            LakonaGameHostingOptions.FromConfiguration(configuration),
            configuration);
    }

    /// <summary>
    /// Adds game-server services using an explicit options object.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="options">The game-server hosting options to apply.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddLakonaGameServer(
        this IServiceCollection services,
        LakonaGameHostingOptions options)
    {
        return services.AddLakonaGameServer(options, configuration: null);
    }

    private static IServiceCollection AddLakonaGameServer(
        this IServiceCollection services,
        LakonaGameHostingOptions options,
        IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.TryAddSingleton(options);
        services.TryAddSingleton(new LakonaGameRuntimeOptions());
        services.TryAddSingleton<LakonaEndpointRuntimeRegistry>();
        services.TryAddSingleton(provider =>
        {
            var runtimeOptions = provider.GetRequiredService<LakonaGameRuntimeOptions>();
            return new LocalActorNodeIdentity(new NodeId(runtimeOptions.Node.Id));
        });

        services.AddLakonaGameServerActors(actorOptions => options.Actors.ApplyTo(actorOptions));
        LakonaGameGeneratedServiceRegistrationDiscovery.RegisterDiscovered(services);
        services.AddLakonaGameServerSessionCleanup(sessionOptions =>
        {
            options.Sessions.Cleanup.ApplyTo(sessionOptions);
        });

        if (configuration is null)
        {
            services.AddLakonaGameServerReliablePush();
        }
        else
        {
            services.AddLakonaGameServerReliablePush(configuration);
        }

        services.AddLogging();
        services.AddLakonaGameRuntimeValidation();
        services.AddLakonaGameHealth();
        services.AddLakonaGameSessionHotfixLifecycle();
        services.AddLakonaTimers();
        services.TryAddSingleton<IHotfixCandidateRollbackParticipant, ActorHostingHotfixRollbackParticipant>();
        services.TryAddSingleton<HotfixActorLifecycleInvoker>();
        services.Replace(ServiceDescriptor.Singleton<IActorLifecycleDispatcher, HotfixActorLifecycleDispatcher>());
        services.TryAddSingleton<IGameHandshakeService, GameHandshakeService>();
        services.TryAddSingleton(new ActorHostDescriptorCatalog([]));
        services.TryAddSingleton(new StartupActorDescriptorCatalog([]));
        services.TryAddSingleton<ILakonaGameServer, DefaultLakonaGameServer>();
        services.TryAddSingleton<StartupActorHostedService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHotfixRuntimePublicationParticipant, StartupActorPublicationParticipant>());
        services.TryAddSingleton<ClusterMembershipState>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RpcServersHostedService>());
        services.TryAddSingleton<DistributedWorkAdmissionGate>();
        services.TryAddSingleton<IDistributedWorkAdmissionGate>(provider =>
            provider.GetRequiredService<DistributedWorkAdmissionGate>());
        services.TryAddSingleton<IClusterMembership>(provider =>
            provider.GetRequiredService<ClusterMembershipState>());
        services.TryAddSingleton<IMembershipTable>(provider => CreateMembershipTable(
            provider.GetRequiredService<LakonaGameRuntimeOptions>(),
            configuration));
        services.TryAddSingleton(provider =>
        {
            var runtime = provider.GetRequiredService<LakonaGameRuntimeOptions>();
            return new MembershipTableManager(
                new NodeId(runtime.Node.Id),
                NodeIncarnationId.New(),
                new NodeEndpoint(runtime.Cluster.Endpoint),
                provider.GetRequiredService<IMembershipTable>(),
                provider.GetRequiredService<ClusterMembershipState>());
        });
        services.TryAddSingleton<IClusterMembershipRefresher>(provider =>
            provider.GetRequiredService<MembershipTableManager>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<MembershipTableHostedService>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<StartupActorHostedService>());
        services.TryAddSingleton<IClusterNodeDescriptorRefresher, ClusterMembershipDescriptorRefresher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LakonaServerStartupHostedService>());
        if (configuration is null)
        {
            services.TryAddSingleton(provider =>
                provider.GetRequiredService<LakonaGameRuntimeOptions>().ToClusterOptions());
        }
        else
        {
            services.TryAddSingleton(provider =>
                provider.GetRequiredService<LakonaGameRuntimeOptions>().ToClusterOptions(configuration));
        }

        services.AddLakonaGameClusterEndpoint();
        services.TryAddSingleton<MembershipProbeHandler>();
        services.TryAddSingleton<IMembershipProbeHandler>(provider =>
            provider.GetRequiredService<MembershipProbeHandler>());
        services.TryAddSingleton<MembershipTableHostedService>();
        return services;
    }

    private static IMembershipTable CreateMembershipTable(
        LakonaGameRuntimeOptions runtime,
        IConfiguration? configuration)
    {
        var options = runtime.Cluster.Membership;
        if (string.Equals(options.Provider, LakonaGameMembershipOptions.MemoryProvider, StringComparison.OrdinalIgnoreCase))
        {
            return new InMemoryMembershipTable();
        }

        if (!string.Equals(options.Provider, LakonaGameMembershipOptions.PostgresProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown Lakona membership provider '{options.Provider}'.");
        }

        var connectionString = configuration?.GetConnectionString(options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{options.ConnectionStringName} is required by the PostgreSQL membership provider.");
        }

        return new PostgresMembershipTable(NpgsqlDataSource.Create(connectionString));
    }
}
