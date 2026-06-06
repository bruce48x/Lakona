using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Lakona.Rpc.Core
{
    public sealed class TransformingTransport : ITransport, IRemoteEndPointProvider
    {
        private readonly ITransport _inner;
        private readonly TransportFrameCodec _codec;

        public TransformingTransport(ITransport inner, TransportSecurityConfig config)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _codec = new TransportFrameCodec(config ?? throw new ArgumentNullException(nameof(config)));
        }

        public EndPoint? RemoteEndPoint => (_inner as IRemoteEndPointProvider)?.RemoteEndPoint;

        public bool IsConnected => _inner.IsConnected;

        public ValueTask ConnectAsync(CancellationToken ct = default)
        {
            return _inner.ConnectAsync(ct);
        }

        public ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct = default)
        {
            return SendCoreAsync(frame, ct);
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct = default)
        {
            var raw = await _inner.ReceiveFrameAsync(ct).ConfigureAwait(false);
            if (raw.IsEmpty)
                return raw;

            if (_codec.IsPassthrough)
                return raw;

            using (raw)
            {
                return _codec.Decode(raw);
            }
        }

        public ValueTask DisposeAsync()
        {
            return _inner.DisposeAsync();
        }

        private async ValueTask SendCoreAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            if (_codec.IsPassthrough)
            {
                await _inner.SendFrameAsync(frame, ct).ConfigureAwait(false);
                return;
            }

            using var encoded = _codec.Encode(frame.Span);
            await _inner.SendFrameAsync(encoded.Memory, ct).ConfigureAwait(false);
        }
    }
}
