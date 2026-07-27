namespace Lakona.Game.Cluster.Rpc
{
    public enum ClusterDependencyStatus
    {
        Healthy = 0,
        Timeout = 1,
        Unhealthy = 2
    }
}
