using System.Net;
using Lakona.Rpc.Transport.Kcp;

namespace Lakona.Rpc.Transport.Tests;

public class KcpListenerTests
{
    [Fact]
    public async Task Constructor_DnsHostname_DoesNotThrow()
    {
        await using var listener = new KcpListener(port: 0, host: "localhost");
        Assert.NotNull(listener);
    }

    [Fact]
    public async Task Constructor_LiteralIPv4_DoesNotThrow()
    {
        await using var listener = new KcpListener(port: 0, host: "127.0.0.1");
        Assert.NotNull(listener);
    }

    [Fact]
    public void Constructor_InvalidHost_ThrowsHelpfulException()
    {
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            _ = new KcpListener(port: 0, host: "invalid-host-xyzzy.invalid");
        });

        Assert.Contains("invalid-host-xyzzy.invalid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Constructor_LiteralIPv6_DoesNotThrow()
    {
        await using var listener = new KcpListener(port: 0, host: "::1");
        Assert.NotNull(listener);
        Assert.NotNull(listener.LocalEndPoint);
    }
}

public class KcpConnectionAcceptorTests
{
    [Fact]
    public async Task ListenAddress_CachesFormattedValue()
    {
        await using var acceptor = new KcpConnectionAcceptor(port: 0);
        var first = acceptor.ListenAddress;
        var second = acceptor.ListenAddress;

        // ListenAddress should be stable — same value on repeated access.
        Assert.Equal(first, second);
        Assert.Contains("udp://", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0.0.0.0:0", first, StringComparison.Ordinal);
    }
}
