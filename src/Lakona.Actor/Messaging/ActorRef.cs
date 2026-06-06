using Lakona.Actor.Messaging;

namespace Lakona.Actor;

public sealed class ActorRef<TMessage>
{
    private readonly ActorRef inner;

    internal ActorRef(ActorRef inner)
    {
        this.inner = inner;
    }

    public ActorId Id => inner.Id;

    public ValueTask Send(TMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return inner.Send(message, cancellationToken);
    }

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

    public override string ToString() => inner.ToString();
}
