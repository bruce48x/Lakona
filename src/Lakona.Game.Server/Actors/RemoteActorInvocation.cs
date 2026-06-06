using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorInvocation
{
    public RemoteActorInvocation(
        NodeId node,
        ActorId actorId,
        string actorName,
        string methodName,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset deadline,
        string correlationId)
    {
        Node = node;
        ActorId = actorId;
        ActorName = actorName;
        MethodName = methodName;
        Payload = payload.ToArray();
        Deadline = deadline;
        CorrelationId = correlationId;
    }

    public NodeId Node { get; }

    public ActorId ActorId { get; }

    public string ActorName { get; }

    public string MethodName { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public DateTimeOffset Deadline { get; }

    public string CorrelationId { get; }
}
