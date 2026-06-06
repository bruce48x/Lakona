using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public class ActorCallException : Exception
{
    public ActorCallException(
        ActorCallStatus status,
        ActorId actorId,
        string actorName,
        string methodName,
        string message,
        NodeId? node = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base($"Actor call failed with status {status}. Actor={actorId.Value}, Method={actorName}.{methodName}. {message}", innerException)
    {
        Status = status;
        ActorId = actorId;
        ActorName = actorName ?? throw new ArgumentNullException(nameof(actorName));
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
        Node = node;
        CorrelationId = correlationId;
    }

    public ActorCallStatus Status { get; }

    public ActorId ActorId { get; }

    public string ActorName { get; }

    public string MethodName { get; }

    public NodeId? Node { get; }

    public string? CorrelationId { get; }
}

public sealed class ActorNotFoundException : ActorCallException
{
    public ActorNotFoundException(
        ActorId actorId,
        string actorName,
        string methodName,
        string message,
        NodeId? node = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(ActorCallStatus.ActorNotFound, actorId, actorName, methodName, message, node, correlationId, innerException)
    {
    }
}

public sealed class ActorAlreadyExistsException : ActorCallException
{
    public ActorAlreadyExistsException(
        ActorId actorId,
        string actorName,
        string methodName,
        string message,
        NodeId? node = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(ActorCallStatus.ActorAlreadyExists, actorId, actorName, methodName, message, node, correlationId, innerException)
    {
    }
}

public sealed class ActorOwnershipMismatchException : ActorCallException
{
    public ActorOwnershipMismatchException(
        ActorId actorId,
        string actorName,
        string methodName,
        string message,
        NodeId? node = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(ActorCallStatus.ActorOwnershipMismatch, actorId, actorName, methodName, message, node, correlationId, innerException)
    {
    }
}

public sealed class NodeUnavailableException : ActorCallException
{
    public NodeUnavailableException(
        ActorId actorId,
        string actorName,
        string methodName,
        string message,
        NodeId? node = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(ActorCallStatus.NodeUnavailable, actorId, actorName, methodName, message, node, correlationId, innerException)
    {
    }
}

public sealed class ActorCallTimeoutException : ActorCallException
{
    public ActorCallTimeoutException(
        ActorId actorId,
        string actorName,
        string methodName,
        string message,
        NodeId? node = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(ActorCallStatus.Timeout, actorId, actorName, methodName, message, node, correlationId, innerException)
    {
    }
}

public sealed class ActorBackpressureException : ActorCallException
{
    public ActorBackpressureException(
        ActorId actorId,
        string actorName,
        string methodName,
        string message,
        NodeId? node = null,
        string? correlationId = null,
        Exception? innerException = null)
        : base(ActorCallStatus.Backpressure, actorId, actorName, methodName, message, node, correlationId, innerException)
    {
    }
}
