namespace Lakona.Game.Cluster
{
    public enum NodeHeartbeatStatus
    {
        Refreshed = 0,
        NodeNotFound = 1,
        EpochMismatch = 2,
        Expired = 3
    }
}
