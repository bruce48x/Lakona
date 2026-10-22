using System.Net;
using System.Net.Sockets;
using Lakona.Rpc.Transport.Kcp;

namespace Lakona.Rpc.Transport.Tests;

public class KcpListenerTests
{
    [Fact]
    public void PublicApi_DoesNotExposeTransportHandshakeAdmission()
    {
        var transportAssembly = typeof(KcpListener).Assembly;

        Assert.Null(transportAssembly.GetType("Lakona.Rpc.Transport.Kcp.KcpHandshakeAdmission"));
        Assert.DoesNotContain(
            typeof(KcpListener).GetConstructors().Concat(typeof(KcpConnectionAcceptor).GetConstructors()),
            constructor => constructor.GetParameters().Any(
                parameter => parameter.ParameterType.Name.Contains("HandshakeAdmission", StringComparison.Ordinal)));
    }

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
        // Avoid relying on NXDOMAIN: DNS proxies may synthesize .invalid answers.
        const string host = "invalid/host";
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new KcpListener(port: 0, host: host);
        });

        Assert.Contains(host, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<SocketException>(ex.InnerException);
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
