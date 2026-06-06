using System.Buffers.Binary;

namespace Lakona.Rpc.Transport.Kcp
{
    internal static class KcpHandshake
    {
        private static ReadOnlySpan<byte> RequestMagic => "UKCP"u8;
        private static ReadOnlySpan<byte> AckMagic => "UACK"u8;

        public static bool TryParseRequest(ReadOnlySpan<byte> packet, out uint conv)
        {
            conv = 0;
            if (packet.Length != 8)
                return false;

            if (!packet.Slice(0, 4).SequenceEqual(RequestMagic))
                return false;

            conv = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4));
            return conv != 0;
        }

        public static byte[] CreateAck(uint conv, int sessionPort)
        {
            var buffer = new byte[12];
            AckMagic.CopyTo(buffer);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), conv);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8, 4), sessionPort);
            return buffer;
        }
    }
}
