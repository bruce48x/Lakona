using System.Threading;
using System.Threading.Tasks;
using Lakona.Game.Cluster;
using Lakona.Rpc.Core;

namespace Lakona.Game.Cluster.Rpc;

/// <summary>
/// Provides both outbound connections and the inbound listener for one cluster RPC transport.
/// </summary>
public interface IClusterRpcTransport
{
    /// <summary>
    /// Gets the URI scheme handled by this transport.
    /// </summary>
    string Scheme { get; }

    /// <summary>
    /// Connects to a remote cluster endpoint.
    /// </summary>
    ValueTask<ITransport> ConnectAsync(
        RouteLocation target,
        ClusterEndpoint endpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts listening on the local cluster endpoint.
    /// </summary>
    ValueTask<IRpcConnectionAcceptor> ListenAsync(
        ClusterEndpoint endpoint,
        CancellationToken cancellationToken = default);
}
