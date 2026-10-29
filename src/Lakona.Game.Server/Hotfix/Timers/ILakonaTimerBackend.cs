using System.ComponentModel;

namespace Lakona.Game.Server.Hotfix.Timers;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface ILakonaTimerBackend
{
    TimerId CreateTimer<TActor, TBehavior, TArgs>(
        TActor actor,
        Func<TBehavior, ActorTimerCallback<TActor, TArgs>> selector,
        TimeSpan dueTime,
        TimeSpan? period,
        TArgs args)
        where TActor : global::Lakona.Game.Server.Actors.Actor where TBehavior : class;

    TimerId CreateTimer<TActor, TBehavior>(TActor actor,
        Func<TBehavior, ActorTimerCallback<TActor>> selector, TimeSpan dueTime, TimeSpan? period)
        where TActor : global::Lakona.Game.Server.Actors.Actor where TBehavior : class =>
        throw new NotSupportedException("This backend does not support timers without arguments.");

    void DestroyTimer(global::Lakona.Game.Server.Actors.Actor actor, TimerId timerId);
}
