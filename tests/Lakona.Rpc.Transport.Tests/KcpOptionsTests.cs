using System.Net;
using System.Reflection;
using Lakona.Rpc.Transport.Kcp;

namespace Lakona.Rpc.Transport.Tests;

public sealed class KcpOptionsTests
{
    [Theory]
    [InlineData(9, 128, 128, 0)]
    [InlineData(5001, 128, 128, 0)]
    [InlineData(20, 0, 128, 0)]
    [InlineData(20, 513, 128, 0)]
    [InlineData(20, 128, 47, 0)]
    [InlineData(20, 128, 65536, 0)]
    [InlineData(20, 128, 128, -1)]
    public void Invalid_options_fail_before_socket_start(int interval, int send, int receive, int resend)
    {
        var options = new KcpTransportOptions
        {
            UpdateIntervalMilliseconds = interval, SendWindowSize = send,
            ReceiveWindowSize = receive, FastResend = resend
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => new KcpTransport("127.0.0.1", 1, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KcpListener(0, options));
    }

    [Fact]
    public async Task Options_are_snapshotted_and_different_peer_settings_interoperate()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var options = new KcpTransportOptions
        {
            UpdateIntervalMilliseconds = 10, SendWindowSize = 256, ReceiveWindowSize = 256,
            NoDelay = true, FastResend = 2, CongestionControl = false
        };
        await using var listener = new KcpListener(0, options);
        var legacy = new KcpTransportOptions { UpdateIntervalMilliseconds = 100, SendWindowSize = 32 };
        await using var client = new KcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndPoint!).Port, 123, legacy);
        // Invalid mutations would fail connection/session creation if either endpoint kept the original.
        options.ReceiveWindowSize = 1;
        legacy.UpdateIntervalMilliseconds = -1;
        await client.ConnectAsync(deadline.Token);
        await using var server = (await listener.AcceptAsync(deadline.Token)).Transport;
        Assert.Equal(100u, ProtocolValue(client, "interval"));
        Assert.Equal(32u, ProtocolValue(client, "snd_wnd"));
        Assert.Equal(10u, ProtocolValue(server, "interval"));
        Assert.Equal(256u, ProtocolValue(server, "snd_wnd"));
        Assert.Equal(256u, ProtocolValue(server, "rcv_wnd"));
        Assert.Equal(1u, ProtocolValue(server, "nodelay"));
        Assert.Equal(2u, ProtocolValue(server, "fastresend"));
        Assert.Equal(1u, ProtocolValue(server, "nocwnd"));
        var payload = new byte[256 * 1024];
        new Random(42).NextBytes(payload);
        var receive = server.ReceiveFrameAsync(deadline.Token).AsTask();
        await client.SendFrameAsync(payload, deadline.Token);
        using var frame = await receive;
        Assert.True(frame.Span.SequenceEqual(payload));
        receive = client.ReceiveFrameAsync(deadline.Token).AsTask();
        await server.SendFrameAsync(payload, deadline.Token);
        using var reply = await receive;
        Assert.True(reply.Span.SequenceEqual(payload));
    }

    // The pinned KCP dependency exposes tuning methods but no settings getter.
    // Inspect the actual protocol instance to catch silently ignored options.
    private static uint ProtocolValue(object transport, string name)
    {
        var kcp = transport.GetType().GetField("_kcp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transport)!;
        for (Type? type = kcp.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null) return Convert.ToUInt32(field.GetValue(kcp));
        }
        throw new InvalidOperationException($"KCP field {name} not found.");
    }
}
