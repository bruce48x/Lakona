using System;
using System.Buffers;

namespace Lakona.Rpc.Core
{
    /// <summary>
    ///     Network framing: uint32 length prefix (big-endian) + payload bytes.
    ///     Matches Unity client's LengthPrefix for wire compatibility.
    /// </summary>
    public static class LengthPrefix
    {
        public const int DefaultMaxFrameSize = RpcProtocolLimits.DefaultMaxTransportFrameSize;

        public static TransportFrame Pack(ReadOnlySpan<byte> payload)
        {
            ValidateFrameLength(payload.Length, DefaultMaxFrameSize);
            var frame = TransportFrame.Allocate(4 + payload.Length);
            var buf = frame.GetWritableSpan();
            var len = (uint)payload.Length;
            buf[0] = (byte)(len >> 24);
            buf[1] = (byte)(len >> 16);
            buf[2] = (byte)(len >> 8);
            buf[3] = (byte)len;
            payload.CopyTo(buf.Slice(4));
            return frame;
        }

        public static bool TryUnpack(ref ReadOnlySequence<byte> seq, out ReadOnlySequence<byte> payload)
        {
            return TryUnpack(ref seq, out payload, DefaultMaxFrameSize);
        }

        public static bool TryUnpack(ref ReadOnlySequence<byte> seq, out ReadOnlySequence<byte> payload,
            int maxFrameSize)
        {
            payload = default;
            if (seq.Length < 4) return false;

            Span<byte> hdr = stackalloc byte[4];
            seq.Slice(0, 4).CopyTo(hdr);
            var len = ((uint)hdr[0] << 24) | ((uint)hdr[1] << 16) | ((uint)hdr[2] << 8) | hdr[3];

            if (len > maxFrameSize)
                throw new InvalidOperationException($"Frame too large: {len} bytes");

            if (seq.Length < 4 + (long)len) return false;

            payload = seq.Slice(4, len);
            seq = seq.Slice(4 + len);
            return true;
        }

        public static void ValidateFrameLength(int payloadLength, int maxFrameSize)
        {
            if (payloadLength < 0)
                throw new ArgumentOutOfRangeException(nameof(payloadLength));

            if (maxFrameSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFrameSize));

            if (payloadLength > maxFrameSize)
                throw new InvalidOperationException($"Frame too large: {payloadLength} bytes");
        }
    }
}
