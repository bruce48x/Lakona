using System.Net;
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
        // A clearly unresolvable hostname should throw, but with a meaningful
        // message containing the hostname, not a raw FormatException.
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            _ = new TcpConnectionAcceptor(port: 0, host: "invalid-host-xyzzy.invalid");
        });

        Assert.Contains("invalid-host-xyzzy.invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
