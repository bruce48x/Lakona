namespace Lakona.Game.Cluster
{
    public enum ClusterSendStatus
    {
        Accepted = 0,
        Expired = 1,
        RouteNotFound = 2,
        Backpressure = 3,
        HandlerUnavailable = 4,
        Timeout = 5,
        Failed = 6,
        StaleRoute = 7,
        NodeEpochMismatch = 8,
        TargetNotFound = 9,
        NodeUnavailable = 10,
        SerializationFailed = 11,
        DeserializationFailed = 12,
        Rejected = 13
    }
}
