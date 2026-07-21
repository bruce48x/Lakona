namespace Lakona.Game.Cluster
{
    public enum ClusterMemberState
    {
        Joining = 0,
        Recovering = 1,
        Ready = 2,
        Draining = 3,
        Suspect = 4,
        Fenced = 5
    }
}
