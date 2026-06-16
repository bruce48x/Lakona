using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Transport.Tcp;
using Lakona.Game.Server.Sessions;

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
        if (_runtimeOptions.Cluster is null || string.IsNullOrWhiteSpace(_runtimeOptions.Cluster.Endpoint))
        {
            throw new InvalidOperationException("Lakona:Cluster:Endpoint is required for the cluster RPC server.");
        }

        var endpoint = ClusterEndpoint.Parse(_runtimeOptions.Cluster.Endpoint);
        if (!string.Equals(endpoint.Scheme, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cluster endpoint transport '{endpoint.Scheme}' is not supported by the default cluster RPC server.");
        }

        context.Builder.UseSerializer(new JsonRpcSerializer());
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

        if (context.Services.GetService(typeof(IFeatureMessageHandler)) is IFeatureMessageHandler featureHandler)
        {
            FeatureMessageBinder.Bind(context.Builder.ServiceRegistry, featureHandler);
        }

        if (context.Services.GetService(typeof(LocalClientNotificationCommandDispatcher)) is
            LocalClientNotificationCommandDispatcher notificationDispatcher)
        {
            ClientNotificationCommandBinder.Bind(context.Builder.ServiceRegistry, notificationDispatcher);
        }
    }
}
