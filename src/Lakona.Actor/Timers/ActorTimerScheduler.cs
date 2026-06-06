using System.Diagnostics;
using Lakona.Actor.Core;
using Lakona.Actor.Messaging;

namespace Lakona.Actor.Timers;

internal sealed class ActorTimerScheduler
{
    private readonly ActorRef self;
    private readonly ActorCell cell;

    internal ActorTimerScheduler(ActorRef self, ActorCell cell)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(cell);

        this.self = self;
        this.cell = cell;
    }

    internal IDisposable ScheduleOnce(object message, TimeSpan dueTime)
    {
        return ScheduleRepeated(message, dueTime, Timeout.InfiniteTimeSpan);
    }

    internal IDisposable ScheduleRepeated(object message, TimeSpan dueTime, TimeSpan period)
    {
        ActorTimer timer = new(self, message, dueTime, period, Activity.Current?.Context ?? default);
        cell.AddTimer(timer);
        timer.Start();
        return timer;
    }
}
