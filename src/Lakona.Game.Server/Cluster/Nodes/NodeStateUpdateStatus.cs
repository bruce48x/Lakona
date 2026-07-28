namespace Lakona.Game.Cluster
{
    public enum NodeStateUpdateStatus
    {
        Updated = 0,
        NodeNotFound = 1,
        EpochMismatch = 2,
        Expired = 3
    }
}
