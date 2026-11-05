using System.Diagnostics;
using System.Threading.Channels;
using Lakona.Rpc.Client;
using Lakona.Rpc.Core;
using Lakona.Rpc.Serializer.Json;
using Lakona.Rpc.Server;

namespace Lakona.Rpc.Tests;

public sealed class RpcKeepAliveBlockedWriteTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(3);
    private static readonly RpcKeepAliveOptions KeepAlive = new()
    {
        Enabled = true, Interval = TimeSpan.FromMilliseconds(30), Timeout = TimeSpan.FromMilliseconds(240)
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Client_times_out_and_joins_a_blocked_probe_or_request(bool blockRequest, bool synchronousPing)
    {
        var peer = new BlockedWriteTransport(blockRequest ? RpcFrameType.Request : RpcFrameType.KeepAlivePing)
        {
            BlockSynchronously = synchronousPing
        };
        await using var client = new RpcClientRuntime(peer, new JsonRpcSerializer(), KeepAlive);
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnects = 0;
        client.Disconnected += error => { Interlocked.Increment(ref disconnects); disconnected.TrySetResult(error); };
        await client.StartAsync();
        var request = client.CallAsync(new RpcMethod<int, int>(1, 1), 42).AsTask();
        try
        {
            await peer.WriteStarted.Task.WaitAsync(Guard);
            await peer.WriteCanceled.Task.WaitAsync(Guard);
            Assert.True(client.TimedOutByKeepAlive);
            Assert.Equal(RpcClientConnectionState.Stopped, client.ConnectionState);
            Assert.Equal(blockRequest ? 0 : 1, peer.PingsSinceBlocked);
            Assert.False(disconnected.Task.IsCompleted);
            Assert.False(peer.WriteReturned.Task.IsCompleted);
        }
        finally { peer.ReleaseWrite.TrySetResult(); }

        var timeout = Assert.IsType<TimeoutException>(await disconnected.Task.WaitAsync(Guard));
        Assert.Same(timeout, await Assert.ThrowsAsync<TimeoutException>(() => request.WaitAsync(Guard)));
        Assert.True(await peer.WriteReturned.Task.WaitAsync(Guard));
        await client.DisposeAsync();
        Assert.Equal(1, disconnects);
        Assert.Equal(1, peer.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Server_times_out_and_joins_a_blocked_probe_or_response(bool blockResponse)
    {
        var peer = new BlockedWriteTransport(blockResponse ? RpcFrameType.Response : RpcFrameType.KeepAlivePing)
        {
            // Both cancellation callbacks and the eventual write can fail after the timeout.
            ReturnWriteError = !blockResponse,
            ThrowOnWriteCancellation = !blockResponse
        };
        var registry = new RpcServiceRegistry();
        registry.Register(1, 1, (_, request, _) => new ValueTask<RpcServerResponse>(
            RpcServerResponse.Encode(request.RequestId, RpcStatus.Ok, Array.Empty<byte>())));
        await using var session = new RpcSession(peer, new JsonRpcSerializer(), registry,
            connectionId: "blocked-write", ownsTransport: true, keepAlive: KeepAlive);
        var disconnected = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnects = 0;
        session.Disconnected += error => { Interlocked.Increment(ref disconnects); disconnected.TrySetResult(error); };
        await session.StartAsync();
        if (blockResponse) peer.Enqueue(Request());
        try
        {
            await peer.WriteStarted.Task.WaitAsync(Guard);
            await peer.WriteCanceled.Task.WaitAsync(Guard);
            Assert.Equal(blockResponse ? 0 : 1, peer.PingsSinceBlocked);
            Assert.False(disconnected.Task.IsCompleted);
            Assert.False(peer.WriteReturned.Task.IsCompleted);
        }
        finally { peer.ReleaseWrite.TrySetResult(); }

        Assert.IsType<TimeoutException>(await disconnected.Task.WaitAsync(Guard));
        Assert.True(await peer.WriteReturned.Task.WaitAsync(Guard));
        await session.DisposeAsync();
        Assert.Equal(1, disconnects);
        Assert.Equal(1, peer.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inbound_frames_keep_one_blocked_probe_alive_until_the_peer_falls_silent(bool releaseAfterTraffic)
    {
        await using var peer = new BlockedWriteTransport(RpcFrameType.KeepAlivePing);
        using var channel = new RpcConnectionChannel(peer, KeepAlive);
        using var lifetime = new CancellationTokenSource();
        var timeout = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeouts = 0;
        var probing = channel.RunKeepAliveAsync("Peer timed out.", error =>
        {
            Interlocked.Increment(ref timeouts);
            timeout.TrySetResult(error);
        }, lifetime.Token);
        try
        {
            await peer.WriteStarted.Task.WaitAsync(Guard);
            // Each gap is long enough to make a new probe eligible but short enough to prove liveness.
            var traffic = Stopwatch.StartNew();
            do
            {
                await Task.Delay(KeepAlive.Interval * 2);
                peer.Enqueue(Request());
                using var received = await channel.ReceiveApplicationFrameAsync(lifetime.Token).AsTask().WaitAsync(Guard);
            } while (traffic.Elapsed < KeepAlive.Timeout * 2);
            Assert.False(timeout.Task.IsCompleted);
            Assert.False(peer.WriteCanceled.Task.IsCompleted);

            if (releaseAfterTraffic)
            {
                // A marker behind the blocked send also waits for any incorrectly queued probes.
                using var marker = Request();
                var flushed = channel.SendAsync(marker.Memory).AsTask();
                peer.ReleaseWrite.TrySetResult();
                await flushed.WaitAsync(Guard);
                Assert.Equal(1, peer.PingsSinceBlocked);
            }

            Assert.IsType<TimeoutException>(await timeout.Task.WaitAsync(Guard));
            if (!releaseAfterTraffic)
            {
                await peer.WriteCanceled.Task.WaitAsync(Guard);
                Assert.False(probing.IsCompleted);
                peer.ReleaseWrite.TrySetResult();
            }
            await probing.WaitAsync(Guard);
            Assert.Equal(1, timeouts);
            Assert.True(channel.TimedOut);
            Assert.Equal(releaseAfterTraffic ? 2 : 1, peer.PingsSinceBlocked);
        }
        finally
        {
            lifetime.Cancel();
            peer.ReleaseWrite.TrySetResult();
            await probing.WaitAsync(Guard);
        }
    }

    [Fact]
    public async Task Owner_cancellation_joins_the_blocked_probe_without_reporting_a_timeout()
    {
        await using var peer = new BlockedWriteTransport(RpcFrameType.KeepAlivePing);
        using var channel = new RpcConnectionChannel(peer, KeepAlive);
        using var lifetime = new CancellationTokenSource();
        var timeouts = 0;
        var probing = channel.RunKeepAliveAsync("Peer timed out.", _ => Interlocked.Increment(ref timeouts), lifetime.Token);
        try
        {
            await peer.WriteStarted.Task.WaitAsync(Guard);
            lifetime.Cancel();
            await peer.WriteCanceled.Task.WaitAsync(Guard);
            Assert.False(probing.IsCompleted);
        }
        finally
        {
            lifetime.Cancel();
            peer.ReleaseWrite.TrySetResult();
            await probing.WaitAsync(Guard);
        }
        Assert.False(channel.TimedOut);
        Assert.Equal(0, timeouts);
        Assert.True(await peer.WriteReturned.Task.WaitAsync(Guard));
    }

    private static TransportFrame Request() => RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
    {
        RequestId = 1, ServiceId = 1, MethodId = 1
    });

    private sealed class BlockedWriteTransport(RpcFrameType blockedType) : ITransport
    {
        private readonly Channel<TransportFrame> _incoming = Channel.CreateUnbounded<TransportFrame>();
        private int _pingsSinceBlocked;
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> WriteReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockSynchronously { get; init; }
        public bool ReturnWriteError { get; init; }
        public bool ThrowOnWriteCancellation { get; init; }
        public int PingsSinceBlocked => Volatile.Read(ref _pingsSinceBlocked);
        public int Disposals { get; private set; }
        public bool IsConnected { get; private set; } = true;
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public void Enqueue(TransportFrame frame) => _incoming.Writer.TryWrite(frame);
        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) => _incoming.Reader.ReadAsync(ct);

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            var type = RpcEnvelopeCodec.PeekFrameType(frame.Span);
            if (type == RpcFrameType.KeepAlivePing)
            {
                // An early probe can precede the application write on a busy test machine.
                if (blockedType != type && !WriteStarted.Task.IsCompleted)
                {
                    var ping = RpcEnvelopeCodec.DecodeKeepAlivePing(frame.Span);
                    Enqueue(RpcEnvelopeCodec.EncodeKeepAlivePong(new RpcKeepAlivePongEnvelope
                    {
                        TimestampTicksUtc = ping.TimestampTicksUtc
                    }));
                    return;
                }
                Interlocked.Increment(ref _pingsSinceBlocked);
            }

            if (type != blockedType || !WriteStarted.TrySetResult()) return;
            using var canceled = ct.Register(() =>
            {
                WriteCanceled.TrySetResult();
                if (ThrowOnWriteCancellation) throw new IOException("Injected write cancellation failure.");
            });
            var original = frame.ToArray();
            // The transport still owns its borrow after cancellation until it returns.
            if (BlockSynchronously) ReleaseWrite.Task.GetAwaiter().GetResult();
            else await ReleaseWrite.Task;
            WriteReturned.TrySetResult(frame.Span.SequenceEqual(original));
            if (ReturnWriteError) throw new IOException("Injected late write failure.");
            ct.ThrowIfCancellationRequested();
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            IsConnected = false;
            _incoming.Writer.TryComplete();
            while (_incoming.Reader.TryRead(out var frame)) frame.Dispose();
            return default;
        }
    }
}
