using Lakona.Game.Cluster.Rpc.MemoryPack;
using Lakona.Game.Server.Configuration;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Serializer.MemoryPack;
using Lakona.Rpc.Transport.Kcp;
using Lakona.Rpc.Transport.Tcp;
using Lakona.Rpc.Transport.WebSocket;

namespace Lakona.Game.Server.Hosting;

internal static class LakonaEndpointRuntimeDefaults
{
    public static IRpcSerializer CreateSerializer(LakonaGameEndpointOptions endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return Normalize(endpoint.Serializer) switch
        {
            "json" => new JsonRpcSerializer(),
            "memorypack" => new MemoryPackRpcSerializer(),
            var serializer => throw new InvalidOperationException(
                $"Endpoint serializer '{serializer}' is unknown. Use json or memorypack.")
        };
    }

    public static IRpcSerializer CreateClusterSerializer(LakonaGameClusterOptions cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        return Normalize(cluster.Serializer) switch
        {
            "json" => new JsonRpcSerializer(),
            "memorypack" => ClusterRpcMemoryPack.CreateSerializer(),
            var serializer => throw new InvalidOperationException(
                $"Cluster serializer '{serializer}' is unknown. Use json or memorypack.")
        };
    }

    public static async ValueTask<IRpcConnectionAcceptor> CreateAcceptorAsync(
        LakonaGameEndpointOptions endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return Normalize(endpoint.Transport) switch
        {
            "tcp" => new TcpConnectionAcceptor(endpoint.Port, endpoint.Host),
            "kcp" => new KcpConnectionAcceptor(endpoint.Port, endpoint.Host),
            "websocket" => await WsConnectionAcceptor.CreateAsync(
                endpoint.Port,
                string.IsNullOrWhiteSpace(endpoint.Path) ? endpoint.GetDefaultPath() : endpoint.Path,
                endpoint.Host,
                cancellationToken).ConfigureAwait(false),
            var transport => throw new InvalidOperationException(
                $"Endpoint transport '{transport}' is unknown. Use kcp, tcp, or websocket.")
        };
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim().ToLowerInvariant();
    }
}
