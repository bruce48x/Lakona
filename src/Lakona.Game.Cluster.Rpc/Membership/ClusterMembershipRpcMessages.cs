namespace Lakona.Game.Cluster.Rpc
{
    public sealed class ClusterMembershipFrameRequest
    {
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();
    }

    public sealed class ClusterMembershipFrameReply
    {
        public byte[] Payload { get; set; } = System.Array.Empty<byte>();
    }
}
