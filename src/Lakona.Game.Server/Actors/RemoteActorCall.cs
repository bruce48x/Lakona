using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public static class RemoteActorCall
{
    public static void EnsureAccepted(
        RemoteActorInvocationResult result,
        ActorId actorId,
        string actorName,
        string methodName,
        NodeId? node = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status == RemoteActorStatus.Accepted)
        {
            return;
        }

        Throw(result, actorId, actorName, methodName, node, correlationId);
    }

    public static void EnsureReplied(
        RemoteActorInvocationResult result,
        ActorId actorId,
        string actorName,
        string methodName,
        NodeId? node = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Status == RemoteActorStatus.Replied)
        {
            return;
        }

        Throw(result, actorId, actorName, methodName, node, correlationId);
    }

    public static ActorCallException CreateException(
        RemoteActorInvocationResult result,
        ActorId actorId,
        string actorName,
        string methodName,
        NodeId? node = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var message = result.Message ?? string.Empty;
        return result.Status switch
        {
            RemoteActorStatus.RouteNotFound => new ActorNotFoundException(actorId, actorName, methodName, message, node, correlationId),
            RemoteActorStatus.HandlerUnavailable => new ActorNotFoundException(actorId, actorName, methodName, message, node, correlationId),
            RemoteActorStatus.NodeUnavailable => new NodeUnavailableException(actorId, actorName, methodName, message, node, correlationId),
            RemoteActorStatus.Timeout => new ActorCallTimeoutException(actorId, actorName, methodName, message, node, correlationId),
            RemoteActorStatus.Backpressure => new ActorBackpressureException(actorId, actorName, methodName, message, node, correlationId),
            RemoteActorStatus.Expired => new ActorCallException(ActorCallStatus.Expired, actorId, actorName, methodName, message, node, correlationId),
            _ => new ActorCallException(ActorCallStatus.Failed, actorId, actorName, methodName, message, node, correlationId)
        };
    }

    private static void Throw(
        RemoteActorInvocationResult result,
        ActorId actorId,
        string actorName,
        string methodName,
        NodeId? node,
        string? correlationId)
    {
        if (result.Status == RemoteActorStatus.Cancelled)
        {
            throw new OperationCanceledException(result.Message);
        }

        throw CreateException(result, actorId, actorName, methodName, node, correlationId);
    }
}
