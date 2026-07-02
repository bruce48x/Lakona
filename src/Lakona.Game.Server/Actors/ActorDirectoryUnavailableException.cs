using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class ActorDirectoryUnavailableException : ActorHostingException
{
    public ActorDirectoryUnavailableException(string message)
        : base(ActorId.From("directory/unavailable"), typeof(IActor), "ActorDirectory", message)
    {
    }

    public ActorDirectoryUnavailableException(string message, Exception innerException)
        : base(ActorId.From("directory/unavailable"), typeof(IActor), "ActorDirectory", message, innerException)
    {
    }

    public ActorDirectoryUnavailableException(
        ActorId actorId,
        Type actorType,
        string operation,
        NodeId localNode,
        string message,
        Exception? innerException = null)
        : base(actorId, actorType, operation, message, innerException)
    {
        LocalNode = localNode;
    }

    public NodeId? LocalNode { get; }
}
