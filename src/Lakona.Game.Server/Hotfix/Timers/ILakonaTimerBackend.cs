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
        TArgs args,
        CancellationToken cancellationToken)
        where TActor : global::Lakona.Game.Server.Actors.Actor where TBehavior : class;

    void DestroyTimer(global::Lakona.Game.Server.Actors.Actor actor, TimerId timerId, CancellationToken cancellationToken);
}
