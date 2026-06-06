#if NET10_0_OR_GREATER
using System.Net;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Kcp;

public sealed class KcpConnectionAcceptor : IRpcConnectionAcceptor
{
    private readonly KcpListener _listener;

    public KcpConnectionAcceptor(int port)
        : this(port, RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections)
    {
    }

    public KcpConnectionAcceptor(int port, int maxPendingAcceptedConnections)
        : this(port, maxPendingAcceptedConnections, admission: null)
    {
    }

    public KcpConnectionAcceptor(int port, KcpHandshakeAdmission? admission)
        : this(port, RpcConnectionAdmissionDefaults.MaxPendingAcceptedConnections, admission)
    {
    }

    public KcpConnectionAcceptor(int port, int maxPendingAcceptedConnections, KcpHandshakeAdmission? admission)
    {
        _listener = new KcpListener(port, maxPendingAcceptedConnections, admission);
    }

    public string ListenAddress
    {
        get
        {
            var endPoint = (IPEndPoint?)_listener.LocalEndPoint;
            return $"udp://0.0.0.0:{endPoint?.Port ?? 0}";
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
