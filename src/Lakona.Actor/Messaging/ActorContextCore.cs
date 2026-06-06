using Lakona.Actor.Core;
using Lakona.Actor.Timers;

namespace Lakona.Actor.Messaging;

internal sealed class ActorContextCore
{
    private readonly ActorResponseSlot responseSlot;
    private readonly ActorTimerScheduler timerScheduler;

    internal ActorContextCore(ActorRef self, ActorCell cell, Envelope envelope)
    {
        Self = self;
        responseSlot = new ActorResponseSlot(envelope.Response);
        timerScheduler = new ActorTimerScheduler(self, cell);
    }

    internal ActorRef Self { get; }

    internal bool HasPendingResponse => responseSlot.HasPendingResponse;

    public void Respond<TResponse>(TResponse response)
    {
        responseSlot.Respond(response);
    }

    public bool TryRespond<TResponse>(TResponse response)
    {
        return responseSlot.TryRespond(response);
    }

    public IDisposable ScheduleOnce(object message, TimeSpan dueTime)
    {
        ArgumentNullException.ThrowIfNull(message);

        return timerScheduler.ScheduleOnce(message, dueTime);
    }

    public IDisposable ScheduleRepeated(object message, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(message);

        return timerScheduler.ScheduleRepeated(message, dueTime, period);
    }
}
