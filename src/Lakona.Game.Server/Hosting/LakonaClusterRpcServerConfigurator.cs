using Lakona.Rpc.Server;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Actors;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.ReliablePush;
using Lakona.Rpc.Core;
using Lakona.Game.Server.Sessions;
using Lakona.Game.Cluster.Rpc.Membership;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hosting;

public sealed class LakonaClusterRpcServerConfigurator : IRpcServerConfigurator
{
    private readonly LakonaGameRuntimeOptions _runtimeOptions;

    public LakonaClusterRpcServerConfigurator(LakonaGameRuntimeOptions runtimeOptions)
    {
        _runtimeOptions = runtimeOptions ?? throw new ArgumentNullException(nameof(runtimeOptions));
    }

    public string Transport => "cluster";

    public void Configure(LakonaGameServerRpcContext context)
    {
        if (string.IsNullOrWhiteSpace(_runtimeOptions.Cluster.Endpoint))
        {
            throw new InvalidOperationException("Lakona:Cluster:Endpoint is required for the cluster RPC server.");
        }

        var channel = context.Services.GetRequiredService<ClusterRpcChannel>();
        var endpoint = channel.ParseEndpoint(_runtimeOptions.Cluster.Endpoint);
        var serializer = channel.Serializer;
        var loggerFactory = context.Services.GetService<ILoggerFactory>();
        if (loggerFactory is not null)
        {
            context.Builder.UseLoggerFactory(loggerFactory);
        }

        context.Builder.UseSerializer(serializer);
        context.Builder.UseAcceptor(cancellationToken =>
            channel.ListenAsync(endpoint, cancellationToken));

        if (context.Services.GetService<IClusterMembershipFrameHandler>() is
            IClusterMembershipFrameHandler membershipHandler)
        {
            ClusterMembershipFrameBinder.Bind(
                context.Builder.ServiceRegistry,
                membershipHandler);
        }

        if (context.Services.GetService(typeof(IReliablePushRuntime)) is IReliablePushRuntime reliablePush &&
            context.Services.GetService<IClusterMembership>() is IClusterMembership notificationMembership)
        {
            var ownerDispatcher = new ClientNotificationOwnerDispatcher(
                reliablePush,
                notificationMembership,
                new NodeId(_runtimeOptions.Node.Id),
                context.Services.GetService<IDistributedWorkAdmissionGate>());
            ClientNotificationCommandBinder.BindOwned(context.Builder.ServiceRegistry, ownerDispatcher);
        }

        if (context.Services.GetService<HotfixActorClusterHandler>() is
            HotfixActorClusterHandler actorHandler)
        {
            ClusterActorRpcBinder.Bind(context.Builder.ServiceRegistry, actorHandler);
        }

        if (context.Services.GetService<ActorLocationDirectory>() is
            ActorLocationDirectory actorLocation)
        {
            ActorLocationDirectory.Bind(context.Builder.ServiceRegistry, actorLocation);
        }

        if (context.Services.GetService<ActorLifecycleRpcHandler>() is
            ActorLifecycleRpcHandler actorLifecycle)
        {
            ActorLifecycleRpcHandler.Bind(context.Builder.ServiceRegistry, actorLifecycle);
        }

        if (context.Services.GetService<StartupActorAffinityDirectory>() is { } startupAffinity)
        {
            StartupActorAffinityDirectory.Bind(context.Builder.ServiceRegistry, startupAffinity);
        }

    }
}
