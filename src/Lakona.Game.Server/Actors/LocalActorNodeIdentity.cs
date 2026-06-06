using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class LocalActorNodeIdentity
{
    public LocalActorNodeIdentity(NodeId nodeId)
    {
        NodeId = nodeId;
    }

    public NodeId NodeId { get; }
}
