using Lakona.Game.Cluster;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Tcp;

namespace Lakona.Game.Cluster.Rpc.Transport.Tcp;

/// <summary>
/// Provides TCP connections and listeners for a cluster RPC channel.
/// </summary>
public sealed class TcpClusterRpcTransport : IClusterRpcTransport
{
    /// <summary>
    /// Gets the shared stateless TCP cluster transport adapter.
    /// </summary>
    public static TcpClusterRpcTransport Default { get; } = new();

    private TcpClusterRpcTransport()
    {
    }

    /// <inheritdoc />
    public string Scheme => "tcp";

    /// <inheritdoc />
    public async ValueTask<ITransport> ConnectAsync(
        RouteLocation target,
        ClusterEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(endpoint);
        ValidateScheme(endpoint);

        var transport = new TcpTransport(endpoint.Host, endpoint.Port);
        await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        return transport;
    }

    /// <inheritdoc />
    public ValueTask<IRpcConnectionAcceptor> ListenAsync(
        ClusterEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateScheme(endpoint);
        return new ValueTask<IRpcConnectionAcceptor>(
            new TcpConnectionAcceptor(endpoint.Port, endpoint.Host));
    }

    private void ValidateScheme(ClusterEndpoint endpoint)
    {
        if (!string.Equals(endpoint.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cluster endpoint scheme '{endpoint.Scheme}' does not match the selected '{Scheme}' cluster transport.");
        }
    }
}
