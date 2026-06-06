using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class RemoteActorException : Exception
{
    public RemoteActorException(
        RemoteActorStatus status,
        ActorId actorId,
        string actorName,
        string methodName,
        string message,
        NodeId? node = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base($"Remote actor call failed with status {status}. Actor={actorId.Value}, Method={actorName}.{methodName}. {message}", innerException)
    {
        Status = status;
        ActorId = actorId;
        ActorName = actorName ?? throw new ArgumentNullException(nameof(actorName));
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        Node = node;
        CorrelationId = correlationId;
    }

    public RemoteActorStatus Status { get; }
    public NodeId? Node { get; }
    public ActorId ActorId { get; }
    public string ActorName { get; }
    public string MethodName { get; }
    public string? CorrelationId { get; }
}
