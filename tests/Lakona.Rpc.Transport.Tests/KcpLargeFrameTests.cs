using System.Net;
using System.Net.Sockets.Kcp;
using System.Reflection;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Kcp;

namespace Lakona.Rpc.Transport.Tests;

public sealed class KcpLargeFrameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Large_frame_and_following_marker_arrive_intact(bool serverSends)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await using var listener = new KcpListener(0);
        await using var client = new KcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndPoint!).Port);
        await client.ConnectAsync(deadline.Token);
        await using var server = (await listener.AcceptAsync(deadline.Token)).Transport;
        ITransport sender = serverSends ? server : client;
        ITransport receiver = serverSends ? client : server;
        byte[] payload = new byte[12 * 1024 * 1024];
        new Random(42).NextBytes(payload);
        var receiving = receiver.ReceiveFrameAsync(deadline.Token).AsTask();
        // No ReceiveFrameAsync on the sender: ACK progress must be independent.
        await sender.SendFrameAsync(payload, deadline.Token);
        using var received = await receiving;
        Assert.True(received.Span.SequenceEqual(payload));
        await sender.SendFrameAsync(new byte[] { 91 }, deadline.Token);
        using var marker = await receiver.ReceiveFrameAsync(deadline.Token);
        Assert.Equal(new byte[] { 91 }, marker.ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Stalled_receiver_bounds_queue_and_cancel_or_dispose_ends_partial_send(bool serverSends, bool dispose)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var listener = new KcpListener(0);
        await using var client = new KcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndPoint!).Port);
        await client.ConnectAsync(deadline.Token);
        await using var server = (await listener.AcceptAsync(deadline.Token)).Transport;
        ITransport sender = serverSends ? server : client;
        using var cancellation = new CancellationTokenSource();
        var sending = sender.SendFrameAsync(new byte[12 * 1024 * 1024], cancellation.Token).AsTask();
        await Task.Delay(250, deadline.Token);
        Assert.False(sending.IsCompleted);
        var kcp = (SimpleSegManager.Kcp)sender.GetType().GetField("_kcp", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sender)!;
        var gate = sender.GetType().GetField("_kcpGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sender)!;
        lock (gate) Assert.InRange(kcp.WaitSnd, 1, KcpFrameSender.MaximumPendingSegments);
        if (dispose) await sender.DisposeAsync();
        else cancellation.Cancel();
        var error = await Record.ExceptionAsync(() => sending.WaitAsync(deadline.Token));
        Assert.NotNull(error);
        Assert.False(sender.IsConnected);
        await Assert.ThrowsAnyAsync<Exception>(() => sender.SendFrameAsync(new byte[] { 1 }).AsTask());
    }

    [Fact]
    public async Task Pre_canceled_send_preserves_connection()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var listener = new KcpListener(0);
        await using var client = new KcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndPoint!).Port);
        await client.ConnectAsync(deadline.Token);
        await using var server = (await listener.AcceptAsync(deadline.Token)).Transport;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendFrameAsync(new byte[1024], canceled.Token).AsTask());
        Assert.True(client.IsConnected);
        await client.SendFrameAsync(new byte[] { 7 }, deadline.Token);
        using var received = await server.ReceiveFrameAsync(deadline.Token);
        Assert.Equal(new byte[] { 7 }, received.ToArray());
    }
}
