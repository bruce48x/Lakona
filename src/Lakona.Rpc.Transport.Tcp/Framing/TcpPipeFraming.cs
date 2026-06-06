using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Tcp
{
    internal sealed class TcpPipeFraming : IAsyncDisposable
    {
        private readonly int _maxFrameSize;
        private readonly PipeReader _reader;
        private readonly PipeWriter _writer;

        public TcpPipeFraming(Stream stream, int maxFrameSize)
        {
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            _maxFrameSize = maxFrameSize;
            _reader = PipeReader.Create(stream);
            _writer = PipeWriter.Create(stream);
        }

        public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            LengthPrefix.ValidateFrameLength(frame.Length, _maxFrameSize);
            var header = _writer.GetSpan(4);
            var length = (uint)frame.Length;
            header[0] = (byte)(length >> 24);
            header[1] = (byte)(length >> 16);
            header[2] = (byte)(length >> 8);
            header[3] = (byte)length;
            _writer.Advance(4);

            if (!frame.IsEmpty)
                _writer.Write(frame.Span);

            await _writer.FlushAsync(ct).ConfigureAwait(false);
        }

        public async ValueTask<TransportFrame> ReceiveFrameAsync(CancellationToken ct)
        {
            while (true)
            {
                var result = await _reader.ReadAsync(ct).ConfigureAwait(false);
                var buffer = result.Buffer;
                var remaining = buffer;

                if (LengthPrefix.TryUnpack(ref remaining, out var payload, _maxFrameSize))
                {
                    var frame = CopyPayload(payload);
                    _reader.AdvanceTo(remaining.Start, remaining.Start);
                    return frame;
                }

                if (result.IsCompleted)
                {
                    _reader.AdvanceTo(buffer.End);
                    throw new IOException("Remote closed the connection.");
                }

                _reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _reader.CompleteAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await _writer.CompleteAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private static TransportFrame CopyPayload(in ReadOnlySequence<byte> payload)
        {
            if (payload.IsEmpty)
                return TransportFrame.Empty;

            var frame = TransportFrame.Allocate(checked((int)payload.Length));
            payload.CopyTo(frame.GetWritableSpan());
            return frame;
        }
    }
}
