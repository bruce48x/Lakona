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
    public async Task Dispose_DisconnectsPeerAndRejectsPeerSend()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();
        await server.ConnectAsync();

        await client.DisposeAsync();

        Assert.False(server.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.SendFrameAsync(new byte[] { 1 }).AsTask());

        await server.DisposeAsync();
    }

    [Fact]
    public async Task ConnectAsync_AfterPeerDispose_AllowsEndpointToObserveEof()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.DisposeAsync();

        await server.ConnectAsync();

        Assert.False(server.IsConnected);
        using var received = await server.ReceiveFrameAsync();
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

    [Fact]
    public async Task QueueCapacity_AppliesBackpressureUntilPeerReceives()
    {
        LoopbackTransport.CreatePair(out var client, out var server, queueCapacity: 1);
        await client.ConnectAsync();
        await server.ConnectAsync();

        await client.SendFrameAsync(new byte[] { 1 });
        var blockedSend = client.SendFrameAsync(new byte[] { 2 }).AsTask();

        Assert.False(blockedSend.IsCompleted);
        using var first = await server.ReceiveFrameAsync();
        await blockedSend.WaitAsync(TimeSpan.FromSeconds(5));
        using var second = await server.ReceiveFrameAsync();
        Assert.Equal(new byte[] { 1 }, first.ToArray());
        Assert.Equal(new byte[] { 2 }, second.ToArray());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task QueueCapacity_BlockedSendCanBeCanceledWithoutConsumingCapacity()
    {
        LoopbackTransport.CreatePair(out var client, out var server, queueCapacity: 1);
        await client.ConnectAsync();
        await server.ConnectAsync();
        await client.SendFrameAsync(new byte[] { 1 });
        using var cancellation = new CancellationTokenSource();

        var blockedSend = client.SendFrameAsync(new byte[] { 2 }, cancellation.Token).AsTask();
        Assert.False(blockedSend.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blockedSend);
        using var first = await server.ReceiveFrameAsync();
        await client.SendFrameAsync(new byte[] { 3 });
        using var third = await server.ReceiveFrameAsync();
        Assert.Equal(new byte[] { 1 }, first.ToArray());
        Assert.Equal(new byte[] { 3 }, third.ToArray());

        await client.DisposeAsync();
        await server.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_ReleasesPeerSendWaitingForCapacity()
    {
        LoopbackTransport.CreatePair(out var client, out var server, queueCapacity: 1);
        await client.ConnectAsync();
        await server.ConnectAsync();
        await client.SendFrameAsync(new byte[] { 1 });
        var blockedSend = client.SendFrameAsync(new byte[] { 2 }).AsTask();
        Assert.False(blockedSend.IsCompleted);

        await server.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => blockedSend);
        Assert.False(client.IsConnected);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentPeerDisposalIsIdempotent()
    {
        LoopbackTransport.CreatePair(out var client, out var server);
        await client.ConnectAsync();
        await server.ConnectAsync();

        await Task.WhenAll(client.DisposeAsync().AsTask(), server.DisposeAsync().AsTask())
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(client.IsConnected);
        Assert.False(server.IsConnected);
    }

    [Fact]
    public async Task SendAndPeerDispose_RaceTerminatesWithoutAcceptingLaterFrames()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            LoopbackTransport.CreatePair(out var client, out var server, queueCapacity: 1);
            await client.ConnectAsync();
            await server.ConnectAsync();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var send = Task.Run(async () =>
            {
                await start.Task;
                try
                {
                    await client.SendFrameAsync(new byte[] { 1 });
                }
                catch (InvalidOperationException)
                {
                }
            });
            var close = Task.Run(async () =>
            {
                await start.Task;
                await server.DisposeAsync();
            });

            start.SetResult();
            await Task.WhenAll(send, close).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(client.IsConnected);
            Assert.False(server.IsConnected);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SendFrameAsync(new byte[] { 2 }).AsTask());
            await client.DisposeAsync();
        }
    }
}
