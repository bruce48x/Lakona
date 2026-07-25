using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc.Membership;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Diagnostics;
using Lakona.Game.Server.Guardrails;
using Lakona.Game.Server.Health;
using Lakona.Game.Server.Hosting;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Timers;
using Lakona.Game.Server.Observability;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Hosting;

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
    /// timers, observability, guardrails, and the default <see cref="ILakonaGameServer"/>.
    /// Hosts that use <c>LakonaGameServer.RunAsync</c> normally do not need to call
    /// this method directly. A direct host must register a <c>ClusterRpcChannel</c>
    /// and its adapters explicitly; process-local Actor-only hosts should use
    /// <c>AddLakonaGameServerActors</c> instead.
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
        if (options.Sessions.Cleanup.Enabled)
        {
            services.AddLakonaGameServerSessionCleanup(sessionOptions =>
            {
                options.Sessions.Cleanup.ApplyTo(sessionOptions);
                sessionOptions.ResumeWindow = options.Sessions.ResumeWindow;
            });
        }
        else
        {
            services.AddLakonaGameServerSessions();
            var sessionOptions = new SessionCleanupOptions();
            options.Sessions.Cleanup.ApplyTo(sessionOptions);
            sessionOptions.ResumeWindow = options.Sessions.ResumeWindow;
            services.RemoveAll<SessionCleanupOptions>();
            services.AddSingleton(sessionOptions);
        }

        if (configuration is null)
        {
            services.AddLakonaGameServerReliablePush();
        }
        else
        {
            services.AddLakonaGameServerReliablePush(configuration);
        }

        services.AddMessageRecording();
        services.AddLakonaGameObservability();
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LakonaClusterDirectorySchemaHostedService>());
        services.TryAddSingleton(new ClusterMembershipNodeOptions());
        services.TryAddSingleton<DistributedWorkAdmissionGate>();
        services.TryAddSingleton<IDistributedWorkAdmissionGate>(provider =>
            provider.GetRequiredService<DistributedWorkAdmissionGate>());
        services.TryAddSingleton(provider =>
        {
            var runtime = provider.GetRequiredService<LakonaGameRuntimeOptions>();
            var gate = provider.GetRequiredService<DistributedWorkAdmissionGate>();
            var participants = provider.GetServices<IClusterRecoveryParticipant>();
            var membershipOptions = provider.GetService<ClusterMembershipNodeOptions>();
            var replicated = runtime.Cluster.BootstrapNewCluster || runtime.Cluster.Seeds.Count > 0;
            return replicated
                ? new ReplicatedClusterMembershipHostedService(
                    runtime,
                    gate,
                    participants,
                    provider.GetRequiredService<IClusterMembershipTransport>(),
                    membershipOptions,
                    provider)
                : new ReplicatedClusterMembershipHostedService(
                    runtime,
                    gate,
                    participants,
                    membershipOptions);
        });
        services.TryAddSingleton<IClusterMembership>(provider =>
            provider.GetRequiredService<ReplicatedClusterMembershipHostedService>());
        services.TryAddSingleton<IClusterMembershipFrameHandler>(provider =>
            provider.GetRequiredService<ReplicatedClusterMembershipHostedService>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<ReplicatedClusterMembershipHostedService>());
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<StartupActorHostedService>());
        services.TryAddSingleton<LakonaGameClusterRegistrationHostedService>();
        services.TryAddSingleton<IClusterNodeRegistrationRefresher>(provider => provider.GetRequiredService<LakonaGameClusterRegistrationHostedService>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<LakonaGameClusterRegistrationHostedService>());
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
        return services;
    }
}
