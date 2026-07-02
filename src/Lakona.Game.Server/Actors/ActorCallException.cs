using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

/// <summary>
/// Base exception for generated actor calls and lower-level actor request/reply calls.
/// </summary>
public class ActorCallException : Exception
{
    /// <summary>
    /// Initializes a new actor call exception.
    /// </summary>
    /// <param name="status">The structured actor call failure status.</param>
    /// <param name="actorId">The target actor id.</param>
    /// <param name="actorName">The actor contract name.</param>
    /// <param name="methodName">The actor method name.</param>
    /// <param name="message">The failure detail message.</param>
    /// <param name="node">The target node when the call crossed a cluster boundary.</param>
    /// <param name="correlationId">The call correlation id when available.</param>
    /// <param name="innerException">The underlying exception when available.</param>
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

    /// <summary>
    /// Gets the structured actor call failure status.
    /// </summary>
    public ActorCallStatus Status { get; }

    /// <summary>
    /// Gets the target actor id.
    /// </summary>
    public ActorId ActorId { get; }

    /// <summary>
    /// Gets the actor contract name.
    /// </summary>
    public string ActorName { get; }

    /// <summary>
    /// Gets the actor method name.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// Gets the target node when the call crossed a cluster boundary.
    /// </summary>
    public NodeId? Node { get; }

    /// <summary>
    /// Gets the call correlation id when available.
    /// </summary>
    public string? CorrelationId { get; }
}

/// <summary>
/// Indicates that the requested actor route or local actor instance was not found.
/// </summary>
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

/// <summary>
/// Indicates that the target node for a remote actor call was unavailable.
/// </summary>
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

/// <summary>
/// Indicates that an actor request/reply call timed out.
/// </summary>
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

/// <summary>
/// Indicates that an actor mailbox or cluster route rejected the call because of backpressure.
/// </summary>
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
