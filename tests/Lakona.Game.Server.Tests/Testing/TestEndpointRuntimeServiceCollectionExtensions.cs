using Lakona.Game.Cluster;
using Lakona.Game.Cluster.Rpc;
using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Tests;

namespace Lakona.Game.Server.Tests.Testing;

internal static class TestEndpointRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddTestEndpointRuntimes(this IServiceCollection services)
    {
        return services
            .UseReadySingleNodeMembership()
            .AddLakonaEndpointTransport("tcp", static _ => new UnsupportedConnectionAcceptor("tcp"))
            .AddLakonaEndpointTransport("kcp", static _ => new UnsupportedConnectionAcceptor("kcp"))
            .AddLakonaEndpointTransport("websocket", static _ => new UnsupportedConnectionAcceptor("websocket"))
            .AddLakonaEndpointSerializer("json", static () => new JsonRpcSerializer())
            .AddLakonaEndpointSerializer("memorypack", static () => new MemoryPackRpcSerializer())
            .AddSingleton(new ClusterRpcChannel(
                new UnsupportedClusterTransport(),
                new MemoryPackRpcSerializer(),
                ClusterRpcChannel.ProtocolId));
    }

    private sealed class UnsupportedConnectionAcceptor(string transport) : IRpcConnectionAcceptor
    {
        public string ListenAddress { get; } = $"{transport}://test";

        public ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
        {
            throw new NotSupportedException("The test endpoint acceptor must be replaced before the server is run.");
        }

        public ValueTask DisposeAsync()
        {
            return default;
        }
    }

    private sealed class UnsupportedClusterTransport : IClusterRpcTransport
    {
        public string Scheme => "tcp";

        public ValueTask<ITransport> ConnectAsync(
            RouteLocation target,
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The test cluster transport must be replaced before a connection is opened.");

        public ValueTask<IRpcConnectionAcceptor> ListenAsync(
            ClusterEndpoint endpoint,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The test cluster transport must be replaced before the server is run.");
    }
}
