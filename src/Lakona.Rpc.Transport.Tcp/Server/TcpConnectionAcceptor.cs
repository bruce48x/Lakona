#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Sockets;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tcp;

public sealed class TcpConnectionAcceptor : IRpcConnectionAcceptor
{
    private readonly TcpListener _listener;

    public TcpConnectionAcceptor(int port)
        : this(port, "127.0.0.1")
    {
    }

    public TcpConnectionAcceptor(int port, string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host is required.", nameof(host));

        var bindAddress = IPAddress.TryParse(host, out var parsed)
            ? parsed
            : IPAddress.Parse(host);

        _listener = new TcpListener(bindAddress, port);
        _listener.Start();
    }

    public string ListenAddress
    {
        get
        {
            var endPoint = (IPEndPoint)_listener.LocalEndpoint;
            return $"tcp://{endPoint.Address}:{endPoint.Port}";
        }
    }

    public async ValueTask<RpcAcceptedConnection> AcceptAsync(CancellationToken ct = default)
    {
        var client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
        var remoteEndPoint = client.Client.RemoteEndPoint;
        return new RpcAcceptedConnection(
            new TcpServerTransport(client),
            remoteEndPoint?.ToString() ?? "?",
            remoteEndPoint);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return default;
    }
}
#endif
