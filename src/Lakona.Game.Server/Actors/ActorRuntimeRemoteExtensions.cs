using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public static class ActorRuntimeRemoteExtensions
{
    private static readonly NodeEndpoint PlaceholderEndpoint = new("local");

    public static async ValueTask<TResult> AskRemoteAsync<TResult>(
        this IActorRuntime runtime,
        IClusterRouter router,
        RemoteActorGateway gateway,
        IRouteDirectory routeDirectory,
        NodeId localNode,
        string actorId,
        string kind,
        Func<ReadOnlyMemory<byte>> serializeRequest,
        Func<ReadOnlyMemory<byte>, TResult> deserializeResult,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(routeDirectory);
        ArgumentNullException.ThrowIfNull(serializeRequest);
        ArgumentNullException.ThrowIfNull(deserializeResult);

        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor id is required.", nameof(actorId));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        var correlationId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var replyRoute = ClusterActorRouteKeys.ForReply(localNode);
        var replyLocation = new RouteLocation(
            replyRoute,
            localNode,
            PlaceholderEndpoint,
            now.Add(timeout));

        await routeDirectory.RegisterAsync(replyLocation, cancellationToken).ConfigureAwait(false);

        var pendingTask = gateway.RegisterPendingAsync(correlationId, timeout, cancellationToken);

        var envelope = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor(actorId),
            actorId,
            kind,
            serializeRequest(),
            now.Add(timeout),
            localNode,
            replyCorrelationId: correlationId);

        var status = await router.SendAsync(
            envelope.ToClusterMessage(),
            cancellationToken).ConfigureAwait(false);

        if (status != ClusterSendStatus.Accepted)
        {
            throw new InvalidOperationException(
                $"Remote actor call failed with status: {status}. ActorId={actorId}, Kind={kind}");
        }

        var replyPayload = await pendingTask.ConfigureAwait(false);
        return deserializeResult(replyPayload);
    }

    public static async ValueTask TellRemoteAsync(
        this IActorRuntime runtime,
        IClusterRouter router,
        NodeId localNode,
        string actorId,
        string kind,
        Func<ReadOnlyMemory<byte>> serializePayload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(serializePayload);

        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor id is required.", nameof(actorId));
        }

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        var now = DateTimeOffset.UtcNow;
        var envelope = new ClusterActorEnvelope(
            ClusterActorRouteKeys.ForActor(actorId),
            actorId,
            kind,
            serializePayload(),
            now.Add(timeout),
            localNode);

        var status = await router.SendAsync(
            envelope.ToClusterMessage(),
            cancellationToken).ConfigureAwait(false);

        if (status != ClusterSendStatus.Accepted)
        {
            throw new InvalidOperationException(
                $"Remote actor tell failed with status: {status}. ActorId={actorId}, Kind={kind}");
        }
    }
}
