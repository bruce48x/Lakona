namespace Lakona.Game.Cluster
{
    public enum NodeState
    {
        Starting = 0,
        Ready = 1,
        Draining = 2,
        Suspect = 3,
        Dead = 4
    }
}
