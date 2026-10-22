using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class LocalActorNodeIdentity
{
    private NodeReference? reference;
    private NodeReference? stoppingReference;

    public LocalActorNodeIdentity(NodeId nodeId)
    {
        NodeId = nodeId;
    }

    public NodeId NodeId { get; }

    public NodeReference? Reference => Volatile.Read(ref reference);

    // Set only after the Membership Table has committed Stopping. The routing
    // snapshot deliberately omits Stopping members, so absence alone is no proof.
    internal NodeReference? StoppingReference => Volatile.Read(ref stoppingReference);

    internal void ConfirmStopping(NodeReference value)
    {
        if (value != Reference)
            throw new InvalidOperationException("Stopping must refer to the exact local incarnation.");
        Volatile.Write(ref stoppingReference, value);
    }

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
