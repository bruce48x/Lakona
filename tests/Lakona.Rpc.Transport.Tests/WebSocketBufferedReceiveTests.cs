using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using Lakona.Rpc.Core;
using Lakona.Rpc.Transport.WebSocket;
using NetWebSocket = System.Net.WebSockets.WebSocket;

namespace Lakona.Rpc.Transport.Tests;

public sealed class WebSocketBufferedReceiveTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(12)]
    public async Task Maximum_frame_preserves_following_prefix_or_payload(int followingBytes)
    {
        using var socket = new BoundarySocket(MaximumFrameWithTail(), followingBytes);
        await using var receiver = new WsServerTransport(socket);
        using var first = await receiver.ReceiveFrameAsync();
        using var second = await receiver.ReceiveFrameAsync();
        Assert.Equal(LengthPrefix.DefaultMaxFrameSize, first.Length);
        Assert.Equal(-1, first.Span.IndexOfAnyExcept((byte)42));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, second.ToArray());
        FrameLimitTestAssertions.UsesDerivedLengthPrefixedBudgets(receiver);
        Assert.Equal(0, FrameLimitTestAssertions.GetAccumulator(receiver).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Maximum_frame_and_following_frame_cross_a_socket_read(bool serverReceives) =>
        WithConnectionAsync(serverReceives, async (receiver, peer, ct) =>
        {
            var wire = MaximumFrameWithTail();
            var boundary = RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize - 1;
            var receiving = receiver.ReceiveFrameAsync(ct).AsTask();
            try
            {
                await peer.SendAsync(wire.AsMemory(0, boundary), WebSocketMessageType.Binary, false, ct);
                await WaitForPartialAsync(receiver, boundary, receiving, ct);
                await peer.SendAsync(wire.AsMemory(boundary), WebSocketMessageType.Binary, true, ct);
                using var first = await receiving.WaitAsync(ct);
                using var second = await receiver.ReceiveFrameAsync(ct);
                Assert.Equal(LengthPrefix.DefaultMaxFrameSize, first.Length);
                Assert.Equal(-1, first.Span.IndexOfAnyExcept((byte)42));
                Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, second.ToArray());
                Assert.Equal(0, FrameLimitTestAssertions.GetAccumulator(receiver).Count);
            }
            finally
            {
                if (!receiving.IsCompleted) await receiver.DisposeAsync();
                await ObserveAsync(receiving);
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Oversized_frame_is_rejected_from_its_prefix(bool serverReceives) =>
        WithConnectionAsync(serverReceives, async (receiver, peer, ct) =>
        {
            var prefix = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)LengthPrefix.DefaultMaxFrameSize + 1);
            await peer.SendAsync(prefix.AsMemory(), WebSocketMessageType.Binary, true, ct);
            await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.ReceiveFrameAsync(ct).AsTask());
        });

    private static byte[] MaximumFrameWithTail()
    {
        var length = RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize;
        var wire = new byte[length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(wire, (uint)LengthPrefix.DefaultMaxFrameSize);
        wire.AsSpan(4, LengthPrefix.DefaultMaxFrameSize).Fill(42);
        BinaryPrimitives.WriteUInt32BigEndian(wire.AsSpan(length), 8);
        for (var i = 0; i < 8; i++) wire[length + 4 + i] = (byte)(i + 1);
        return wire;
    }

    [Fact]
    public async Task Buffered_frames_do_not_request_another_socket_read()
    {
        using var socket = new SingleBatchSocket(Pack([11], [22], [33]));
        await using var receiver = new WsServerTransport(socket);
        using var first = await receiver.ReceiveFrameAsync();
        using var second = await receiver.ReceiveFrameAsync();
        using var third = await receiver.ReceiveFrameAsync();
        Assert.Equal(new byte[] { 11 }, first.ToArray());
        Assert.Equal(new byte[] { 22 }, second.ToArray());
        Assert.Equal(new byte[] { 33 }, third.ToArray());
        Assert.Equal(1, socket.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Merged_frames_are_delivered_while_the_peer_is_silent(bool serverReceives) =>
        WithConnectionAsync(serverReceives, async (receiver, peer, ct) =>
        {
            await peer.SendAsync(Pack([11], [22, 23], [33, 34, 35]).AsMemory(), WebSocketMessageType.Binary, true, ct);
            using var first = await receiver.ReceiveFrameAsync(ct);
            using var second = await receiver.ReceiveFrameAsync(ct);
            using var third = await receiver.ReceiveFrameAsync(ct);
            Assert.Equal(new byte[] { 11 }, first.ToArray());
            Assert.Equal(new byte[] { 22, 23 }, second.ToArray());
            Assert.Equal(new byte[] { 33, 34, 35 }, third.ToArray());
        });

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    public Task Complete_frames_precede_a_partial_prefix_or_payload(bool serverReceives, int partialBytes) =>
        WithConnectionAsync(serverReceives, async (receiver, peer, ct) =>
        {
            byte[] payload = [30, 31, 32, 33, 34, 35, 36, 37];
            var last = Pack(payload);
            var batch = Pack([10], [20]).Concat(last.Take(partialBytes)).ToArray();
            await peer.SendAsync(batch.AsMemory(), WebSocketMessageType.Binary, true, ct);
            using var first = await receiver.ReceiveFrameAsync(ct);
            using var second = await receiver.ReceiveFrameAsync(ct);
            var receiving = receiver.ReceiveFrameAsync(ct).AsTask();
            try
            {
                await WaitForPartialAsync(receiver, partialBytes, receiving, ct);
                Assert.Equal(new byte[] { 10 }, first.ToArray());
                Assert.Equal(new byte[] { 20 }, second.ToArray());
                await peer.SendAsync(last.AsMemory(partialBytes), WebSocketMessageType.Binary, true, ct);
                using var third = await receiving.WaitAsync(ct);
                Assert.Equal(payload, third.ToArray());
            }
            finally
            {
                if (!receiving.IsCompleted) await receiver.DisposeAsync();
                await ObserveAsync(receiving);
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Buffered_frames_are_delivered_before_observing_the_peer_close(bool serverReceives) =>
        WithConnectionAsync(serverReceives, async (receiver, peer, ct) =>
        {
            await peer.SendAsync(Pack([11], [22]).AsMemory(), WebSocketMessageType.Binary, true, ct);
            await peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", ct);
            using var first = await receiver.ReceiveFrameAsync(ct);
            using var second = await receiver.ReceiveFrameAsync(ct);
            await Assert.ThrowsAsync<IOException>(() => receiver.ReceiveFrameAsync(ct).AsTask());
            await receiver.DisposeAsync();
            Assert.Equal(new byte[] { 11 }, first.ToArray());
            Assert.Equal(new byte[] { 22 }, second.ToArray());
            Assert.Empty((byte[])Field(FrameLimitTestAssertions.GetAccumulator(receiver), "_buffer"));
        });

    [Theory]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    [InlineData(false, "close")]
    [InlineData(true, "close")]
    [InlineData(false, "dispose")]
    [InlineData(true, "dispose")]
    public Task Partial_receive_terminates_without_invalidating_previously_returned_frames(bool serverReceives, string completion) =>
        WithConnectionAsync(serverReceives, async (receiver, peer, ct) =>
        {
            var partial = Pack(new byte[128 * 1024]);
            const int retained = 80 * 1024;
            var batch = Pack([11, 12, 13], [21, 22, 23]).Concat(partial.Take(retained)).ToArray();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var firstRead = receiver.ReceiveFrameAsync(ct).AsTask();
            await peer.SendAsync(batch.AsMemory(), WebSocketMessageType.Binary, true, ct);
            using var first = await firstRead.WaitAsync(ct);
            using var second = await receiver.ReceiveFrameAsync(ct);
            using var slice = second.Slice(1, 1);
            var receiving = receiver.ReceiveFrameAsync(cancellation.Token).AsTask();
            try
            {
                await WaitForPartialAsync(receiver, retained, receiving, ct);
                if (completion == "cancel") cancellation.Cancel();
                else if (completion == "close")
                    await peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "partial", ct);
                else
                {
                    var firstDispose = receiver.DisposeAsync().AsTask();
                    var secondDispose = receiver.DisposeAsync().AsTask();
                    Assert.Same(firstDispose, secondDispose);
                    await Task.WhenAll(firstDispose, secondDispose).WaitAsync(ct);
                }
                var error = await Record.ExceptionAsync(async () => { using var frame = await receiving.WaitAsync(ct); });
                if (completion == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
                else if (completion == "close") Assert.IsType<IOException>(error);
                else Assert.True(error is IOException or ObjectDisposedException or OperationCanceledException or WebSocketException, error?.ToString());
                await receiver.DisposeAsync().AsTask().WaitAsync(ct);
                Assert.False(receiver.IsConnected);
                var accumulator = FrameLimitTestAssertions.GetAccumulator(receiver);
                Assert.Equal(0, accumulator.Count);
                Assert.Empty((byte[])Field(accumulator, "_buffer"));
                Assert.Equal(new byte[] { 11, 12, 13 }, first.ToArray());
                second.Dispose();
                Assert.Equal(new byte[] { 22 }, slice.ToArray());
                await Assert.ThrowsAsync<ObjectDisposedException>(() => receiver.ReceiveFrameAsync(ct).AsTask());
            }
            finally
            {
                cancellation.Cancel();
                await ObserveAsync(receiving);
            }
        });

    private static async Task WaitForPartialAsync(ITransport receiver, int count, Task<TransportFrame> receiving, CancellationToken ct)
    {
        var accumulator = FrameLimitTestAssertions.GetAccumulator(receiver);
        while (accumulator.Count < count)
        {
            Assert.False(receiving.IsCompleted);
            await Task.Delay(1, ct);
        }
        Assert.Equal(count, accumulator.Count);
        Assert.False(receiving.IsCompleted);
    }

    private static async Task ObserveAsync(Task<TransportFrame> receive) =>
        await Record.ExceptionAsync(async () => { using var frame = await receive; });

    private static byte[] Pack(params byte[][] payloads)
    {
        using var stream = new MemoryStream();
        foreach (var payload in payloads)
        {
            using var frame = LengthPrefix.Pack(payload);
            stream.Write(frame.Span);
        }
        return stream.ToArray();
    }

    private static async Task WithConnectionAsync(bool serverReceives, Func<ITransport, NetWebSocket, CancellationToken, Task> action)
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
        var receiver = serverReceives ? server : client;
        var sender = serverReceives ? client : server;
        await action(receiver, (NetWebSocket)Field(sender, "_webSocket"), ct);
    }

    private static object Field(object value, string name) =>
        value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;

    private sealed class BoundarySocket(byte[] wire, int followingBytes) : NetWebSocket
    {
        private int offset;
        private WebSocketState state = WebSocketState.Open;
        public override WebSocketState State => state;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override void Abort() => state = WebSocketState.Aborted;
        public override void Dispose() => state = WebSocketState.Closed;
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
        { state = WebSocketState.Closed; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) => throw new NotSupportedException();
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var frameEnd = RpcProtocolLimits.DefaultMaxLengthPrefixedFrameSize;
            // Deliver all but the final frame byte, then offer that byte together
            // with a following prefix/payload. Respect the caller's read capacity.
            var segmentEnd = offset < frameEnd - 1 ? frameEnd - 1 :
                offset < frameEnd + followingBytes ? frameEnd + followingBytes : wire.Length;
            var count = Math.Min(buffer.Count, segmentEnd - offset);
            Assert.True(count > 0, "The receiver must make progress without reading past complete frames.");
            wire.AsSpan(offset, count).CopyTo(buffer.AsSpan());
            offset += count;
            return Task.FromResult(new WebSocketReceiveResult(count, WebSocketMessageType.Binary, offset == wire.Length));
        }
    }

    private sealed class SingleBatchSocket(byte[] wire) : NetWebSocket
    {
        private WebSocketState state = WebSocketState.Open;
        public int Reads { get; private set; }
        public override WebSocketState State => state;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override string? SubProtocol => null;
        public override void Abort() => state = WebSocketState.Aborted;
        public override void Dispose() => state = WebSocketState.Closed;
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct)
        { state = WebSocketState.Closed; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) => throw new NotSupportedException();
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
        {
            if (++Reads != 1) throw new InvalidOperationException("A complete buffered frame must not wait for another socket read.");
            wire.AsSpan().CopyTo(buffer.AsSpan());
            return Task.FromResult(new WebSocketReceiveResult(wire.Length, WebSocketMessageType.Binary, true));
        }
    }
}
