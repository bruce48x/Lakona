using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets.Kcp;
using System.Runtime.ExceptionServices;
using Lakona.Rpc.Core;

namespace Lakona.Rpc.Transport.Kcp
{

    // One writer, shared with the transport's Input/Update/Dispose lock. WaitSnd includes
    // both queued and unacknowledged segments; the protocol window alone does not bound it.
    internal sealed class KcpFrameSender
    {
        internal const int ChunkBytes = 64 * 1024;
        internal const int MaximumPendingSegments = 512;
        private const int SegmentBytes = KcpConst.IKCP_MTU_DEF - KcpConst.IKCP_OVERHEAD;
        private readonly SimpleSegManager.Kcp _kcp;
        private readonly object _gate;
        private readonly Action<DateTimeOffset> _reschedule;
        private readonly Action<Exception> _fail;
        private readonly SemaphoreSlim _changed = new(0, 1);
        private ExceptionDispatchInfo? _stopped;

        public KcpFrameSender(SimpleSegManager.Kcp kcp, object gate,
            Action<DateTimeOffset> reschedule, Action<Exception> fail)
        {
            _kcp = kcp;
            _gate = gate;
            _reschedule = reschedule;
            _fail = fail;
        }

        public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LengthPrefix.ValidateFrameLength(frame.Length, LengthPrefix.DefaultMaxFrameSize);
            var totalLength = checked(4 + frame.Length);
            var firstLength = Math.Min(ChunkBytes, totalLength);
            var first = ArrayPool<byte>.Shared.Rent(firstLength);
            BinaryPrimitives.WriteUInt32BigEndian(first, (uint)frame.Length);
            frame.Span.Slice(0, firstLength - 4).CopyTo(first.AsSpan(4));
            var submitted = false;
            try
            {
                var offset = 0;
                while (offset < totalLength)
                {
                    ct.ThrowIfCancellationRequested();
                    var length = Math.Min(ChunkBytes, totalLength - offset);
                    DateTimeOffset next = default;
                    var sent = false;
                    lock (_gate)
                    {
                        _stopped?.Throw();
                        if (_kcp.WaitSnd + (length + SegmentBytes - 1) / SegmentBytes <= MaximumPendingSegments)
                        {
                            // A failed send or Output callback may already have submitted bytes.
                            submitted = true;
                            var result = _kcp.Send(offset == 0 ? first.AsSpan(0, length) : frame.Span.Slice(offset - 4, length), null!);
                            if (result != length)
                                throw new IOException($"KCP accepted {result} of {length} bytes.");
                            var now = DateTimeOffset.UtcNow;
                            _kcp.Update(in now);
                            next = _kcp.Check(in now);
                            sent = true;
                        }
                    }
                    if (sent)
                    {
                        offset += length;
                        _reschedule(next);
                    }
                    else
                        await _changed.WaitAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                if (submitted) _fail(error);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(first);
            }
        }

        // Called after Input under the same gate, so checking capacity and waiting
        // cannot lose an ACK. There is at most one waiting writer.
        public void Signal()
        {
            // Serialize producers; the single reader can only decrease CurrentCount.
            // A pending signal already covers the latest capacity/terminal state.
            lock (_gate)
            {
                if (_changed.CurrentCount == 0) _changed.Release();
            }
        }

        public void Stop(Exception error)
        {
            lock (_gate) _stopped ??= ExceptionDispatchInfo.Capture(error);
            Signal();
        }
    }
}
