using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.Kcp;

namespace Lakona.Rpc.Transport.Tests;

[Collection("KCP receive buffer ownership")]
public sealed class KcpClientReceiveLifetimeTests
{
    [Fact]
    public async Task Pending_receive_buffer_is_not_returned_before_socket_abort()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        await using var client = new KcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndPoint!).Port);
        await client.ConnectAsync(deadline.Token);
        await using var server = (await listener.AcceptAsync(deadline.Token)).Transport;

        // Inspect the actual socket handle only to observe the resource lifetime at pool return.
        var socket = Assert.IsType<Socket>(typeof(KcpTransport)
            .GetField("_socket", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client));
        var handle = socket.SafeHandle;
        using var pool = new ReceiveBufferObserver(() => handle.IsClosed);
        pool.CaptureRental = true;
        var receive = client.ReceiveFrameAsync().AsTask();
        pool.CaptureRental = false;
        Assert.False(receive.IsCompleted);
        Assert.True(pool.SawRental);
        Assert.Equal(0, pool.ReturnCount);

        await client.DisposeAsync();
        var error = await Record.ExceptionAsync(async () =>
        {
            using var frame = await receive.WaitAsync(deadline.Token);
        });

        Assert.True(receive.IsCompleted);
        Assert.True(error is ObjectDisposedException or SocketException or OperationCanceledException,
            error?.ToString());
        Assert.Equal(1, pool.ReturnCount);
        Assert.False(pool.ReturnedBeforeSocketClosed);
    }

    [Fact]
    public async Task Canceling_receive_releases_its_buffer_and_allows_the_next_receive()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        await using var client = new KcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndPoint!).Port);
        await client.ConnectAsync(deadline.Token);
        await using var server = (await listener.AcceptAsync(deadline.Token)).Transport;
        using (var pool = new ReceiveBufferObserver(() => false))
        using (var cancellation = new CancellationTokenSource())
        {
            pool.CaptureRental = true;
            var receive = client.ReceiveFrameAsync(cancellation.Token).AsTask();
            pool.CaptureRental = false;
            Assert.True(pool.SawRental);
            Assert.False(receive.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receive.WaitAsync(deadline.Token));
            Assert.Equal(1, pool.ReturnCount);
        }

        Assert.True(client.IsConnected);
        byte[] payload = [1, 2, 3];
        await server.SendFrameAsync(payload, deadline.Token);
        using var frame = await client.ReceiveFrameAsync(deadline.Token);
        Assert.Equal(payload, frame.ToArray());
    }

    [Fact]
    public async Task Incoming_frames_racing_disposal_do_not_access_released_kcp_state()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var listener = new KcpListener(new IPEndPoint(IPAddress.Loopback, 0));
        var payload = Enumerable.Range(0, 1024).Select(i => (byte)i).ToArray();
        for (var iteration = 0; iteration < 32; iteration++)
        {
            await using var client = new KcpTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndPoint!).Port);
            await client.ConnectAsync(deadline.Token);
            await using var server = (await listener.AcceptAsync(deadline.Token)).Transport;
            var receive = client.ReceiveFrameAsync(deadline.Token).AsTask();
            await server.SendFrameAsync(payload, deadline.Token);
            await client.DisposeAsync();
            var error = await Record.ExceptionAsync(async () =>
            {
                using var frame = await receive.WaitAsync(deadline.Token);
                Assert.Equal(payload, frame.ToArray());
            });
            Assert.True(receive.IsCompleted);
            Assert.True(error is null or ObjectDisposedException or SocketException or OperationCanceledException,
                error?.ToString());
            Assert.False(client.IsConnected);
        }
    }

    private sealed class ReceiveBufferObserver(Func<bool> socketClosed) : EventListener
    {
        private readonly int _callerThread = Environment.CurrentManagedThreadId;
        private int _bufferId;
        private int _returnCount;
        public bool CaptureRental { get; set; }
        public bool SawRental => _bufferId != 0;
        public int ReturnCount => Volatile.Read(ref _returnCount);
        public bool ReturnedBeforeSocketClosed { get; private set; }

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "System.Buffers.ArrayPoolEventSource")
                EnableEvents(source, EventLevel.Verbose);
        }

        protected override void OnEventWritten(EventWrittenEventArgs data)
        {
            if (data.Payload is not { Count: >= 2 }) return;
            if (data.EventName == "BufferRented" && CaptureRental
                && Environment.CurrentManagedThreadId == _callerThread && (int)data.Payload[1]! == 64 * 1024)
                _bufferId = (int)data.Payload[0]!;
            if (data.EventName == "BufferReturned" && _bufferId != 0 && (int)data.Payload[0]! == _bufferId)
            {
                ReturnedBeforeSocketClosed |= !socketClosed();
                Interlocked.Increment(ref _returnCount);
            }
        }
    }
}

// Pool events are process-wide; isolate these observations from unrelated rentals.
[CollectionDefinition("KCP receive buffer ownership", DisableParallelization = true)]
public sealed class KcpReceiveBufferOwnershipCollection
{
}
