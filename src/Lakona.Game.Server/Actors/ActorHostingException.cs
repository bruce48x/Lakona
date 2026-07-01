using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public class ActorHostingException : InvalidOperationException
{
    public ActorHostingException(
        ActorId actorId,
        Type actorType,
        string operation,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ActorId = actorId;
        ActorType = actorType;
        Operation = operation;
    }

    public ActorId ActorId { get; }

    public Type ActorType { get; }

    public string Operation { get; }
}

public sealed class ActorAlreadyHostedException : ActorHostingException
{
    public ActorAlreadyHostedException(ActorId actorId, Type actorType, string operation)
        : base(
            actorId,
            actorType,
            operation,
            $"Actor id '{actorId.Value}' is already hosted locally as '{actorType.FullName}'.")
    {
    }
}

public sealed class ActorHostingTypeMismatchException : ActorHostingException
{
    public ActorHostingTypeMismatchException(
        ActorId actorId,
        Type requestedActorType,
        Type existingActorType,
        string operation)
        : base(
            actorId,
            requestedActorType,
            operation,
            $"Actor id '{actorId.Value}' is bound to '{existingActorType.FullName}', not '{requestedActorType.FullName}'.")
    {
        ExistingActorType = existingActorType;
    }

    public Type ExistingActorType { get; }
}

public sealed class ActorHostedElsewhereException : ActorHostingException
{
    public ActorHostedElsewhereException(
        ActorId actorId,
        Type actorType,
        string operation,
        NodeId localNode,
        NodeId ownerNode)
        : base(
            actorId,
            actorType,
            operation,
            $"Actor id '{actorId.Value}' is hosted on node '{ownerNode.Value}', not local node '{localNode.Value}'.")
    {
        LocalNode = localNode;
        OwnerNode = ownerNode;
    }

    public NodeId LocalNode { get; }

    public NodeId OwnerNode { get; }
}

public sealed class ActorHostingStopException : ActorHostingException
{
    public ActorHostingStopException(
        ActorId actorId,
        Type actorType,
        string operation,
        string message,
        Exception? innerException = null)
        : base(actorId, actorType, operation, message, innerException)
    {
    }
}
