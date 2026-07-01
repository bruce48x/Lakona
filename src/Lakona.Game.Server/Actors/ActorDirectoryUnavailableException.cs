using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class ActorDirectoryUnavailableException : Exception
{
    public ActorDirectoryUnavailableException(string message)
        : base(message)
    {
    }

    public ActorDirectoryUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ActorDirectoryUnavailableException(
        ActorId actorId,
        Type actorType,
        string operation,
        NodeId localNode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ActorId = actorId;
        ActorType = actorType;
        Operation = operation;
        LocalNode = localNode;
    }

    public ActorId? ActorId { get; }

    public Type? ActorType { get; }

    public string? Operation { get; }

    public NodeId? LocalNode { get; }
}
