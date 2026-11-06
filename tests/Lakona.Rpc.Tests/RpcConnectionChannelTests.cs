using System.Runtime.CompilerServices;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public sealed class RpcConnectionChannelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeepAlive_PongProcessedBeforePingSendCompletes_DoesNotRestorePendingProbe(bool replyToSecondPing)
    {
        await using var transport = new EarlyPongTransport(replyToSecondPing);
        using var channel = new RpcConnectionChannel(transport, new RpcKeepAliveOptions
        {
            Enabled = true,
            Interval = TimeSpan.FromMilliseconds(10),
            Timeout = TimeSpan.FromMilliseconds(20),
            MeasureRtt = false
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var timedOut = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiving = channel.ReceiveApplicationFrameAsync(cancellation.Token).AsTask();
        var probing = channel.RunKeepAliveAsync("keepalive timed out", error => timedOut.TrySetResult(error), cancellation.Token);
        try
        {
            var outcome = await Task.WhenAny(transport.SecondPing.Task, timedOut.Task).WaitAsync(cancellation.Token);
            Assert.True(outcome == transport.SecondPing.Task,
                "A Pong consumed before the Ping write completed was overwritten by a stale pending probe.");
            Assert.False(channel.TimedOut);
            if (!replyToSecondPing)
            {
                Assert.IsType<TimeoutException>(await timedOut.Task.WaitAsync(cancellation.Token));
                Assert.True(channel.TimedOut);
            }
        }
        finally
        {
            cancellation.Cancel();
            await probing.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private sealed class EarlyPongTransport(bool replyToSecondPing) : ITransport
    {
        private readonly System.Threading.Channels.Channel<TransportFrame> incoming =
            System.Threading.Channels.Channel.CreateUnbounded<TransportFrame>();
        private TaskCompletionSource? pongProcessed;
        private int pings;
        public TaskCompletionSource SecondPing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsConnected => true;
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            var ping = RpcEnvelopeCodec.DecodeKeepAlivePing(frame.Span);
            if (++pings == 2)
            {
                SecondPing.TrySetResult();
                if (!replyToSecondPing) return;
            }
            var processed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref pongProcessed, processed);
            incoming.Writer.TryWrite(RpcEnvelopeCodec.EncodeKeepAlivePong(new RpcKeepAlivePongEnvelope
            {
                TimestampTicksUtc = ping.TimestampTicksUtc
            }));
            // The receiver requests another frame only after it has consumed the Pong.
            await processed.Task.WaitAsync(ct);
        }
        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            Interlocked.Exchange(ref pongProcessed, null)?.TrySetResult();
            return await incoming.Reader.ReadAsync(ct);
        }
        public ValueTask DisposeAsync()
        {
            incoming.Writer.TryComplete();
            while (incoming.Reader.TryRead(out var frame)) frame.Dispose();
            return default;
        }
    }

    [Fact]
    public async Task QueuedCancellationDoesNotWaitForActiveSendOrPublishCanceledFrame()
    {
        await using var transport = new BlockingSendTransport();
        using var channel = new RpcConnectionChannel(transport, RpcKeepAliveOptions.Disabled);
        using var cancellation = new CancellationTokenSource();
        var first = channel.SendAsync(new byte[] { 1 }).AsTask();
        var canceled = channel.SendAsync(new byte[] { 2 }, cancellation.Token).AsTask();
        var third = channel.SendAsync(new byte[] { 3 }).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(first.IsCompleted);
        transport.Release.SetResult();
        await Task.WhenAll(first, third).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 1, 3 }, transport.Sent);
    }

    [Fact]
    public async Task Canceled_burst_releases_queued_frames_while_active_write_is_blocked()
    {
        await using var transport = new BlockingSendTransport();
        using var channel = new RpcConnectionChannel(transport, RpcKeepAliveOptions.Disabled);
        using var cancellation = new CancellationTokenSource();
        var first = channel.SendAsync(new byte[] { 1 }).AsTask();
        var (frames, canceled, live) = QueueBurst(channel, cancellation.Token);
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.WhenAll(canceled).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.All(canceled, send => Assert.True(send.IsCanceled));
            Assert.False(first.IsCompleted);
            Assert.False(live.IsCompleted);
            GC.Collect();
            Assert.Equal(0, frames.Count(reference => reference.IsAlive));
        }
        finally { transport.Release.TrySetResult(); }

        await Task.WhenAll(first, live).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 1, 3, 4, 5 }, transport.Sent);
    }

    // Keep all strong references to the test buffers outside the GC assertion's stack frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference[] Frames, Task[] Canceled, Task Live) QueueBurst(
        RpcConnectionChannel channel, CancellationToken cancellation)
    {
        var frames = new WeakReference[3000];
        var canceled = new Task[frames.Length];
        var live = new List<Task>();
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = new byte[] { 2 };
            frames[i] = new WeakReference(frame);
            canceled[i] = channel.SendAsync(frame, cancellation).AsTask();
            if (i % 1000 == 500)
                live.Add(channel.SendAsync(new byte[] { (byte)(3 + live.Count) }).AsTask());
        }
        return (frames, canceled, Task.WhenAll(live));
    }

    [Fact]
    public async Task Canceling_an_active_send_waits_until_the_transport_releases_the_frame()
    {
        await using var transport = new BlockingSendTransport();
        using var channel = new RpcConnectionChannel(transport, RpcKeepAliveOptions.Disabled);
        using var cancellation = new CancellationTokenSource();
        var active = channel.SendAsync(new byte[] { 1 }, cancellation.Token).AsTask();
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var following = channel.SendAsync(new byte[] { 2 }).AsTask();
        try
        {
            cancellation.Cancel();
            Assert.False(active.IsCompleted);
            Assert.False(following.IsCompleted);
        }
        finally { transport.Release.TrySetResult(); }

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await following.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new byte[] { 2 }, transport.Sent);
    }

    private sealed class BlockingSendTransport : ITransport
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<byte> Sent { get; } = [];
        public bool IsConnected => true;
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (frame.Span[0] == 1)
            {
                Started.TrySetResult();
                await Release.Task;
                ct.ThrowIfCancellationRequested();
            }
            Sent.Add(frame.Span[0]);
        }
        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) => new(TransportFrame.Empty);
        public ValueTask DisposeAsync() { Release.TrySetResult(); return default; }
    }

    [Fact]
    public async Task ReceiveApplicationFrameAsync_HandlesKeepAliveBeforeReturningApplicationFrame()
    {
        var pingTimestamp = DateTimeOffset.UtcNow.UtcTicks;
        var transport = new QueuedTransport(
            RpcEnvelopeCodec.EncodeKeepAlivePing(new RpcKeepAlivePingEnvelope
            {
                TimestampTicksUtc = pingTimestamp
            }),
            RpcEnvelopeCodec.EncodeRequest(new RpcRequestEnvelope
            {
                RequestId = 7,
                ServiceId = 8,
                MethodId = 9
            }));
        using var channel = new RpcConnectionChannel(transport, RpcKeepAliveOptions.Disabled);

        using var applicationFrame = await channel.ReceiveApplicationFrameAsync();

        var request = RpcEnvelopeCodec.DecodeRequest(applicationFrame);
        Assert.Equal((uint)7, request.RequestId);
        Assert.Single(transport.SentFrames);
        var pong = RpcEnvelopeCodec.DecodeKeepAlivePong(transport.SentFrames[0]);
        Assert.Equal(pingTimestamp, pong.TimestampTicksUtc);
    }

    private sealed class QueuedTransport : ITransport
    {
        private readonly Queue<TransportFrame> _frames;

        public QueuedTransport(params TransportFrame[] frames)
        {
            _frames = new Queue<TransportFrame>(frames);
        }

        public List<byte[]> SentFrames { get; } = [];

        public bool IsConnected => true;

        public ValueTask ConnectAsync(CancellationToken ct = default) => default;

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            SentFrames.Add(frame.ToArray());
            return default;
        }

        public ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default) =>
            new(_frames.Dequeue());

        public ValueTask DisposeAsync()
        {
            while (_frames.TryDequeue(out var frame))
                frame.Dispose();

            return default;
        }
    }
}
