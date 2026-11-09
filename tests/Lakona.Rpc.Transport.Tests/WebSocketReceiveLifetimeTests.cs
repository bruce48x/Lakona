using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.WebSocket;
using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace Lakona.Rpc.Transport.Tests;

public sealed class WebSocketReceiveLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disposal_releases_partial_large_frame_after_cancellation_or_during_receive(
        bool serverReceives, bool cancelFirst)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var number = ((IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        await using var listener = await WsConnectionAcceptor.CreateAsync(number, "/ws", "127.0.0.1", 2, ct);
        await using var client = new WsTransport(new Uri(listener.ListenAddress));
        await client.ConnectAsync(ct);
        await using var server = (await listener.AcceptAsync(ct)).Transport;
        ITransport receiver = serverReceives ? server : client;
        ITransport sender = serverReceives ? client : server;
        var raw = (NetWebSocket)PrivateField(sender, "_webSocket");
        var partial = new byte[8 * 1024 * 1024 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(partial, 12 * 1024 * 1024);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var receiving = receiver.ReceiveFrameAsync(cancellation.Token).AsTask();
        try
        {
            await raw.SendAsync(partial.AsMemory(), WebSocketMessageType.Binary, false, ct);
            var accumulator = FrameLimitTestAssertions.GetAccumulator(receiver);
            // Observe actual partial input, not an assumed network/scheduler delay.
            while (accumulator.Count < partial.Length)
            {
                Assert.False(receiving.IsCompleted);
                await Task.Delay(1, ct);
            }
            if (cancelFirst)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving.WaitAsync(ct));
            }
            await receiver.DisposeAsync().AsTask().WaitAsync(ct);
            var error = await Record.ExceptionAsync(async () => { using var frame = await receiving.WaitAsync(ct); });
            Assert.True(error is OperationCanceledException or IOException or ObjectDisposedException or WebSocketException,
                error?.ToString());
            Assert.False(receiver.IsConnected);
            Assert.Equal(0, accumulator.Count);
            Assert.Empty((byte[])PrivateField(accumulator, "_buffer"));
            await receiver.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => receiver.ReceiveFrameAsync(ct).AsTask());
        }
        finally
        {
            cancellation.Cancel();
            await Record.ExceptionAsync(async () => { using var frame = await receiving.WaitAsync(ct); });
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disposal_waits_for_late_receive_and_preserves_a_completed_frame(bool completesFrame, bool bufferedTail)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var payload = new byte[128 * 1024];
        new Random(42).NextBytes(payload);
        using var socket = new DelayedFinalChunkSocket(payload, completesFrame, bufferedTail);
        var callbacks = 0;
        await using var transport = new WsServerTransport(socket, onDispose: () => Interlocked.Increment(ref callbacks));
        var receiving = transport.ReceiveFrameAsync(ct).AsTask();
        await socket.FinalReadEntered.Task.WaitAsync(ct);
        var accumulator = FrameLimitTestAssertions.GetAccumulator(transport);
        var buffered = accumulator.Count;
        Assert.True(buffered > 64 * 1024);
        var disposing = transport.DisposeAsync().AsTask();
        try
        {
            await socket.Aborted.Task.WaitAsync(ct);
            Assert.Same(disposing, transport.DisposeAsync().AsTask());
            Assert.False(disposing.IsCompleted);
            Assert.False(transport.IsConnected);
            Assert.Equal(buffered, accumulator.Count);
            Assert.Equal(0, socket.DisposeCount);
            Assert.Equal(0, Volatile.Read(ref callbacks));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => transport.ReceiveFrameAsync(ct).AsTask());
        }
        finally { socket.ReleaseFinalRead.TrySetResult(); }

        if (completesFrame)
        {
            using var frame = await receiving.WaitAsync(ct);
            using var slice = frame.Slice(1, frame.Length - 2);
            await disposing.WaitAsync(ct);
            // The receive won the race. Its transferred array belongs to the frame,
            // independently of the now-disposed connection and cleared accumulator.
            Assert.True(frame.Span.SequenceEqual(payload));
            frame.Dispose();
            Assert.True(slice.Span.SequenceEqual(payload.AsSpan(1, payload.Length - 2)));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => receiving.WaitAsync(ct));
            await disposing.WaitAsync(ct);
        }
        Assert.Equal(0, accumulator.Count);
        Assert.Empty((byte[])PrivateField(accumulator, "_buffer"));
        await transport.DisposeAsync();
        Assert.Equal(1, socket.DisposeCount);
        Assert.Equal(1, Volatile.Read(ref callbacks));
    }

    private static object PrivateField(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    // Abort deliberately does not finish the last read. The disposal must wait for
    // that I/O to relinquish both its pooled socket buffer and the accumulator.
    private sealed class DelayedFinalChunkSocket : NetWebSocket
    {
        private readonly byte[] _wire;
        private int _offset;
        private WebSocketState _state = WebSocketState.Open;
        public TaskCompletionSource FinalReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFinalRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Aborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }
        public DelayedFinalChunkSocket(byte[] payload, bool completesFrame, bool bufferedTail)
        {
            using var packed = LengthPrefix.Pack(payload);
            _wire = packed.ToArray();
            if (bufferedTail)
            {
                using var tail = LengthPrefix.Pack(new byte[] { 7, 8, 9 });
                _wire = _wire.Concat(tail.ToArray()).ToArray();
            }
            if (!completesFrame) BinaryPrimitives.WriteUInt32BigEndian(_wire, (uint)payload.Length + 1);
        }
        public override WebSocketState State => _state;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override void Abort() { _state = WebSocketState.Aborted; Aborted.TrySetResult(); }
        public override void Dispose() { DisposeCount++; _state = WebSocketState.Closed; }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => throw new InvalidOperationException("A pending receive must be aborted before close handshake.");
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) => throw new NotSupportedException();
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            if (_offset == _wire.Length) throw new IOException("Controlled disconnect after partial frame.");
            var length = Math.Min(buffer.Count, _wire.Length - _offset);
            if (_offset + length == _wire.Length)
            {
                FinalReadEntered.TrySetResult();
                await ReleaseFinalRead.Task.WaitAsync(ct);
            }
            _wire.AsSpan(_offset, length).CopyTo(buffer.AsSpan());
            _offset += length;
            return new WebSocketReceiveResult(length, WebSocketMessageType.Binary, false);
        }
    }
}
