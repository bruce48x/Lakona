using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class LocalActorNodeIdentity
{
    private NodeReference? reference;

    public LocalActorNodeIdentity(NodeId nodeId)
    {
        NodeId = nodeId;
    }

    public NodeId NodeId { get; }

    public NodeReference? Reference => Volatile.Read(ref reference);

    internal void Observe(NodeReference value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Node != NodeId)
            throw new InvalidOperationException(
                $"Node reference '{value}' does not belong to local node '{NodeId.Value}'.");

        var existing = Interlocked.CompareExchange(ref reference, value, null);
        if (existing is not null && existing != value)
            throw new InvalidOperationException(
                $"Local node '{NodeId.Value}' cannot change process incarnation without restarting.");
    }
}
