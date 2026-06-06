using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Loopback;

namespace Lakona.Rpc.Tests;

public class LoopbackTransportTests
{
    [Fact]
    public async Task CreatePair_BothSidesCanCommunicate()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();
        await server.ConnectAsync();

        var data = new byte[] { 1, 2, 3 };
        await client.SendFrameAsync(data);
        var received = await server.ReceiveFrameAsync();

        Assert.Equal(data, received.ToArray());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task CreatePair_BidirectionalCommunication()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();
        await server.ConnectAsync();

        await client.SendFrameAsync(new byte[] { 1 });
        await server.SendFrameAsync(new byte[] { 2 });

        var fromClient = await server.ReceiveFrameAsync();
        var fromServer = await client.ReceiveFrameAsync();

        Assert.Equal(new byte[] { 1 }, fromClient.ToArray());
        Assert.Equal(new byte[] { 2 }, fromServer.ToArray());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task SendBeforeConnect_Throws()
    {
        LoopbackTransport.CreatePair(out var client, out var server);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendFrameAsync(new byte[] { 1 }).AsTask());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task ReceiveBeforeConnect_Throws()
    {
        LoopbackTransport.CreatePair(out var client, out var server);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ReceiveFrameAsync().AsTask());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task IsConnected_FalseBeforeConnect_TrueAfter()
    {
        LoopbackTransport.CreatePair(out var client, out var server);

        Assert.False(client.IsConnected);
        Assert.False(server.IsConnected);

        await client.ConnectAsync();
        Assert.True(client.IsConnected);

        await client.DisposeAsync();
        Assert.False(client.IsConnected);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_CompletesQueue_ReceiveReturnsEmpty()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();
        await server.ConnectAsync();

        await client.DisposeAsync();

        var received = await server.ReceiveFrameAsync();
        Assert.True(received.IsEmpty);

        await server.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();

        await client.DisposeAsync();
        await client.DisposeAsync();

        await server.DisposeAsync();
    }

    [Fact]
    public async Task SendAfterDispose_Throws()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();
        await client.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SendFrameAsync(new byte[] { 1 }).AsTask());

        await server.DisposeAsync();
    }

    [Fact]
    public async Task CancellationToken_CancelsPendingReceive()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();
        await server.ConnectAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            server.ReceiveFrameAsync(cts.Token).AsTask());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task MultipleFrames_ReceivedInOrder()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();
        await server.ConnectAsync();

        for (int i = 0; i < 10; i++)
            await client.SendFrameAsync(new byte[] { (byte)i });

        for (int i = 0; i < 10; i++)
        {
            var frame = await server.ReceiveFrameAsync();
            Assert.Equal(new byte[] { (byte)i }, frame.ToArray());
        }

        await client.DisposeAsync();
        await server.DisposeAsync();
    }
}
