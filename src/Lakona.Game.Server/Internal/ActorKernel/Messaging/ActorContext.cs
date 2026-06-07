using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel;

internal sealed class ActorContext<TMessage>
{
    private readonly ActorContextCore inner;

    internal ActorContext(ActorContextCore inner)
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

    public IDisposable ScheduleOnce(TMessage message, TimeSpan dueTime)
    {
        ArgumentNullException.ThrowIfNull(message);

        return inner.ScheduleOnce(message, dueTime);
    }

    public IDisposable ScheduleRepeated(TMessage message, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(message);

        return inner.ScheduleRepeated(message, dueTime, period);
    }
}
