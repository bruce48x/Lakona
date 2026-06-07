using System.Diagnostics;
using Lakona.Game.Server.Internal.ActorKernel.Core;
using Lakona.Game.Server.Internal.ActorKernel.Messaging;

namespace Lakona.Game.Server.Internal.ActorKernel.Timers;

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
