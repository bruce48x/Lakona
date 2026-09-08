using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public sealed class RpcConnectionChannelTests
{
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

    private sealed class BlockingSendTransport : ITransport
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<byte> Sent { get; } = [];
        public bool IsConnected => true;
        public ValueTask ConnectAsync(CancellationToken ct = default) => default;
        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            if (frame.Span[0] == 1) await Release.Task.WaitAsync(ct);
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
