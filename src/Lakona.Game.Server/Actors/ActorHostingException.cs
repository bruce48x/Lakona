using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

/// <summary>
/// Base exception for actor creation and destruction failures.
/// </summary>
public class ActorHostingException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new actor hosting exception.
    /// </summary>
    /// <param name="actorId">The actor id involved in the operation.</param>
    /// <param name="actorType">The actor implementation type involved in the operation.</param>
    /// <param name="operation">The hosting operation name.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying exception when available.</param>
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

    /// <summary>
    /// Gets the actor id involved in the hosting operation.
    /// </summary>
    public ActorId ActorId { get; }

    /// <summary>
    /// Gets the actor implementation type involved in the hosting operation.
    /// </summary>
    public Type ActorType { get; }

    /// <summary>
    /// Gets the hosting operation that failed.
    /// </summary>
    public string Operation { get; }
}

/// <summary>
/// Indicates that strict actor creation found the same actor id already hosted locally.
/// </summary>
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

/// <summary>
/// Indicates that an actor id is already bound to a different local actor type.
/// </summary>
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

    /// <summary>
    /// Gets the actor type currently bound to the requested id.
    /// </summary>
    public Type ExistingActorType { get; }
}

/// <summary>
/// Indicates that an actor id is registered to another cluster node.
/// </summary>
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

    /// <summary>
    /// Gets the local node that attempted the hosting operation.
    /// </summary>
    public NodeId LocalNode { get; }

    /// <summary>
    /// Gets the node that currently owns the actor id.
    /// </summary>
    public NodeId OwnerNode { get; }
}

/// <summary>
/// Indicates that actor destruction failed while stopping or draining the local actor.
/// </summary>
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
