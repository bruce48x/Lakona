using Lakona.Game.Cluster;

namespace Lakona.Game.Server.Actors;

public sealed class ClusterActorTellDispatcher<TActor> : IClusterMessageHandler
    where TActor : class, IActor
{
    private readonly IActorRuntime _runtime;
    private readonly Func<TActor, ClusterActorEnvelope, CancellationToken, ValueTask> _handler;

    public ClusterActorTellDispatcher(
        IActorRuntime runtime,
        Func<TActor, ClusterActorEnvelope, CancellationToken, ValueTask> handler)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public ValueTask<ClusterSendStatus> HandleAsync(
        ClusterMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (!ClusterActorEnvelope.TryFromClusterMessage(message, out var envelope) ||
            envelope is null)
        {
            return ValueTask.FromResult(ClusterSendStatus.RouteNotFound);
        }

        var actorId = ActorId.From(envelope.ActorId);
        var result = _runtime.TryTell<TActor>(
            actorId,
            (actor, ct) => _handler(actor, envelope, ct),
            cancellationToken);

        return ValueTask.FromResult(MapResult(result));
    }

    private static ClusterSendStatus MapResult(ActorTellResult result)
    {
        return result switch
        {
            ActorTellResult.MailboxFull => ClusterSendStatus.Backpressure,
            ActorTellResult.ActorUnavailable => ClusterSendStatus.HandlerUnavailable,
            _ => ClusterSendStatus.Accepted
        };
    }
}
