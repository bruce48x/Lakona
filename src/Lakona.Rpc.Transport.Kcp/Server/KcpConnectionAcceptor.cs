#if NET10_0_OR_GREATER
using System.Net;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Kcp;

public sealed class KcpConnectionAcceptor : IRpcConnectionAcceptor
{
    private readonly KcpListener _listener;

    public KcpConnectionAcceptor(int port)
        : this(port, "127.0.0.1")
    {
    }

    public KcpConnectionAcceptor(int port, string host)
        : this(port, host, RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections)
    {
    }

    public KcpConnectionAcceptor(int port, int maxPendingAcceptedConnections)
        : this(port, "127.0.0.1", maxPendingAcceptedConnections)
    {
    }

    public KcpConnectionAcceptor(int port, KcpHandshakeAdmission? admission)
        : this(port, "127.0.0.1", RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections, admission)
    {
    }

    public KcpConnectionAcceptor(int port, string host, int maxPendingAcceptedConnections)
        : this(port, host, maxPendingAcceptedConnections, admission: null)
    {
    }

    public KcpConnectionAcceptor(int port, int maxPendingAcceptedConnections, KcpHandshakeAdmission? admission)
        : this(port, "127.0.0.1", maxPendingAcceptedConnections, admission)
    {
    }

    public KcpConnectionAcceptor(int port, string host, int maxPendingAcceptedConnections, KcpHandshakeAdmission? admission)
    {
        _listener = new KcpListener(port, host, maxPendingAcceptedConnections, admission);
    }

    public string ListenAddress
    {
        get
        {
            var endPoint = (IPEndPoint?)_listener.LocalEndPoint;
            var host = endPoint?.Address?.ToString() ?? "0.0.0.0";
            return $"udp://{host}:{endPoint?.Port ?? 0}";
        }
    }

    public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
    {
        var accepted = await _listener.AcceptAsync(ct).ConfigureAwait(false);
        return new RpcAcceptedConnection(
            accepted.Transport,
            $"{accepted.RemoteEndPoint} conv={accepted.ConversationId} localPort={accepted.LocalPort}",
            accepted.RemoteEndPoint);
    }

    public ValueTask DisposeAsync()
    {
        return _listener.DisposeAsync();
    }
}
#endif
