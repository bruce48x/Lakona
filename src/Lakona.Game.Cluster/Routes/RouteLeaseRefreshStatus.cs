namespace Lakona.Game.Cluster
{
    public enum RouteLeaseRefreshStatus
    {
        Refreshed = 0,
        RouteNotFound = 1,
        StaleLocation = 2,
        Expired = 3
    }
}
