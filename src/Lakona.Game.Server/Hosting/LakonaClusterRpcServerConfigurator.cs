using Lakona.Rpc.Server;
using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Tcp;
using Lakona.Game.Server.Sessions;
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

        var endpoint = ClusterEndpoint.Parse(_runtimeOptions.Cluster.Endpoint);
        if (!string.Equals(endpoint.Scheme, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cluster endpoint transport '{endpoint.Scheme}' is not supported by the default cluster RPC server.");
        }

        var serializer = context.Services.GetService<LakonaClusterRpcSerializer>()?.Serializer
            ?? context.Services.GetRequiredService<IRpcSerializer>();
        var loggerFactory = context.Services.GetService<ILoggerFactory>();
        if (loggerFactory is not null)
        {
            context.Builder.UseLoggerFactory(loggerFactory);
        }

        context.Builder.UseSerializer(serializer);
        context.Builder.UseAcceptor(_ => new ValueTask<IRpcConnectionAcceptor>(
            new TcpConnectionAcceptor(endpoint.Port, endpoint.Host)));

        if (context.Services.GetService(typeof(INodeDirectory)) is INodeDirectory nodeDirectory)
        {
            NodeDirectoryBinder.Bind(context.Builder.ServiceRegistry, nodeDirectory);
        }

        if (context.Services.GetService(typeof(IRouteDirectory)) is IRouteDirectory routeDirectory)
        {
            RouteDirectoryBinder.Bind(context.Builder.ServiceRegistry, routeDirectory);
        }

        if (context.Services.GetService(typeof(LocalClientNotificationCommandDispatcher)) is
            LocalClientNotificationCommandDispatcher notificationDispatcher)
        {
            ClientNotificationCommandBinder.Bind(context.Builder.ServiceRegistry, notificationDispatcher);
        }

        var actorHandlers = context.Services.GetServices<IClusterMessageHandler>().ToList();
        if (context.Services.GetService<RemoteActorGateway>() is RemoteActorGateway remoteActorGateway)
        {
            actorHandlers.Insert(0, remoteActorGateway.CreateReplyHandler());
        }

        if (actorHandlers.Count > 0)
        {
            var composite = new CompositeClusterMessageHandler(actorHandlers.ToArray());
            ClusterMessageBinder.Bind(
                context.Builder.ServiceRegistry,
                composite);

            if (context.Services.GetService<ClusterLocalMessageHandler>() is
                ClusterLocalMessageHandler localMessageHandler)
            {
                localMessageHandler.SetHandler(composite);
            }
        }
    }
}
