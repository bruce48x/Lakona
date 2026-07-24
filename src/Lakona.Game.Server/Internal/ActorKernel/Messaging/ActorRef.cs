using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed class ActorRef<TMessage>
{
    private readonly ActorRef inner;

    internal ActorRef(ActorRef inner)
    {
        this.inner = inner;
    }

    public ActorId Id => inner.Id;

    public ActorSendResult TrySend(TMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return inner.TrySend(message);
    }

    public ValueTask<TResponse> Call<TResponse>(
        TMessage request,
        ActorCallOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return inner.Call<TResponse>(request, options, cancellationToken);
    }
}
