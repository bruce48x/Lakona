using Lakona.Rpc.Core;

namespace Lakona.Rpc.Tests;

public sealed class RpcConnectionChannelTests
{
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
