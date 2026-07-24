using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed class ActorKernelContext<TMessage>
{
    private readonly ActorContextCore inner;

    internal ActorKernelContext(ActorContextCore inner)
    {
        this.inner = inner;
        Self = new ActorRef<TMessage>(inner.Self);
    }

    public ActorRef<TMessage> Self { get; }

    public bool HasPendingResponse => inner.HasPendingResponse;

    public void Respond<TResponse>(TResponse response)
    {
        inner.Respond(response);
    }

    public bool TryRespond<TResponse>(TResponse response)
    {
        return inner.TryRespond(response);
    }
}
