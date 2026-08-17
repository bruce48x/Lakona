using System;
using System.Buffers;
using System.Buffers.Binary;

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
            BinaryPrimitives.WriteUInt32BigEndian(buf, checked((uint)payload.Length));
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
            var payloadLength = ReadPayloadLength(hdr, maxFrameSize);

            if (seq.Length < 4 + (long)payloadLength) return false;

            payload = seq.Slice(4, payloadLength);
            seq = seq.Slice(4 + payloadLength);
            return true;
        }

        internal static int ReadPayloadLength(ReadOnlySpan<byte> header, int maxFrameSize)
        {
            if (header.Length < sizeof(uint))
                throw new ArgumentException("A length prefix requires four bytes.", nameof(header));

            if (maxFrameSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFrameSize));

            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length > maxFrameSize)
                throw new InvalidOperationException($"Frame too large: {length} bytes");

            return checked((int)length);
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
