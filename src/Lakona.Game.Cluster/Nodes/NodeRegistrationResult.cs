namespace Lakona.Game.Cluster
{
    public sealed class NodeRegistrationResult
    {
        public NodeRegistrationResult(NodeRegistrationStatus status, NodeRecord? record)
        {
            Status = status;
            Record = record;
        }

        public NodeRegistrationStatus Status { get; }

        public NodeRecord? Record { get; }
    }
}
