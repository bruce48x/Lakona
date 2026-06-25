using System;

namespace Lakona.Game.Cluster.Rpc
{
    public sealed class FeatureSendReply
    {
        public int Status { get; set; }

        public byte[] Payload { get; set; } = Array.Empty<byte>();

        public string? ErrorMessage { get; set; }
    }
}
