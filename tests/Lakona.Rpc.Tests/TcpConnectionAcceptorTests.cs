using System.Net;
using System.Net.Sockets;
using Lakona.Rpc.Transport.Tcp;

namespace Lakona.Rpc.Tests;

public sealed class TcpConnectionAcceptorTests
{
    [Fact]
    public async Task Constructor_DnsHostname_DoesNotThrow()
    {
        // "localhost" is a valid DNS hostname, not a literal IP.
        // The constructor should resolve it via DNS, not throw FormatException.
        await using var acceptor = new TcpConnectionAcceptor(port: 0, host: "localhost");
        Assert.NotNull(acceptor);
    }

    [Fact]
    public async Task Constructor_LiteralIPv4_DoesNotThrow()
    {
        await using var acceptor = new TcpConnectionAcceptor(port: 0, host: "127.0.0.1");
        Assert.NotNull(acceptor);
    }

    [Fact]
    public void Constructor_InvalidHost_ThrowsHelpfulException()
    {
        // A malformed name fails even when a DNS proxy synthesizes addresses
        // for the reserved .invalid domain. Assert the resolution failure path.
        const string host = "invalid/host";
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new TcpConnectionAcceptor(port: 0, host: host);
        });

        Assert.Contains(host, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<SocketException>(ex.InnerException);
    }
}
