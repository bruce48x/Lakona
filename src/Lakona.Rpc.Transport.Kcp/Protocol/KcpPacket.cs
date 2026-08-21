using System.Buffers.Binary;

namespace Lakona.Rpc.Transport.Kcp
{
    internal static class KcpPacket
    {
        private const int HeaderLength = 24;
        private const int PayloadLengthOffset = 20;

        public static bool TryReadConversationId(ReadOnlySpan<byte> packet, out uint conversationId)
        {
            conversationId = 0;
            var offset = 0;
            while (offset < packet.Length)
            {
                var remaining = packet.Length - offset;
                if (remaining < HeaderLength)
                    return false;

                var segment = packet.Slice(offset);
                var segmentConversationId = BinaryPrimitives.ReadUInt32LittleEndian(segment.Slice(0, sizeof(uint)));
                if (segmentConversationId == 0)
                    return false;

                if (conversationId == 0)
                    conversationId = segmentConversationId;
                else if (segmentConversationId != conversationId)
                    return false;

                var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(
                    segment.Slice(PayloadLengthOffset, sizeof(uint)));
                if (payloadLength > remaining - HeaderLength)
                    return false;

                offset += checked(HeaderLength + (int)payloadLength);
            }

            return conversationId != 0;
        }
    }
}
