#if NET10_0_OR_GREATER
using System.Net;
using System.Net.Sockets;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tcp;

public sealed class TcpConnectionAcceptor : IRpcConnectionAcceptor
{
    private readonly TcpListener _listener;
    private readonly string _listenAddress;

    public TcpConnectionAcceptor(int port)
        : this(port, "127.0.0.1")
    {
    }

    public TcpConnectionAcceptor(int port, string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host is required.", nameof(host));

        var bindAddress = ResolveBindAddress(host);

        _listener = new TcpListener(bindAddress, port);
        _listener.Start();
        _listenAddress = FormatListenAddress();
    }

    public string ListenAddress => _listenAddress;

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

    private string FormatListenAddress()
    {
        var endPoint = (IPEndPoint?)_listener.LocalEndpoint;
        var host = endPoint?.Address?.ToString() ?? "0.0.0.0";
        return $"tcp://{host}:{endPoint?.Port ?? 0}";
    }

    private static IPAddress ResolveBindAddress(string host)
    {
        // Literal IP addresses parse directly without touching DNS.
        if (IPAddress.TryParse(host, out var parsed))
            return parsed;

        // Resolve DNS hostnames.
        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(host);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Failed to resolve host '{host}': {ex.Message}", ex);
        }

        if (addresses.Length == 0)
            throw new InvalidOperationException(
                $"No IP addresses found for host '{host}'.");

        return addresses[0];
    }
}
#endif
