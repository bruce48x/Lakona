using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed class ActorHandle<TMessage>
{
    private readonly ActorRef inner;

    internal ActorHandle(ActorRef inner)
    {
        this.inner = inner;
        Ref = new ActorRef<TMessage>(inner);
    }

    public ActorId Id => inner.Id;

    public ActorRef<TMessage> Ref { get; }

    public ValueTask Stop()
    {
        return inner.Stop();
    }

    public ValueTask<ActorStopResult> Stop(TimeSpan drainTimeout)
    {
        return inner.Stop(drainTimeout);
    }

    public MailboxMetrics GetMailboxMetrics()
    {
        return inner.GetMailboxMetrics();
    }

    public ActorState GetState()
    {
        return inner.GetState();
    }

    public override string ToString() => inner.ToString();
}
