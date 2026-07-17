using System.ComponentModel;

namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

public delegate ValueTask HotfixTimerCallback<TArgs>(TimerTick<TArgs> tick);

[EditorBrowsable(EditorBrowsableState.Never)]
public interface IHotfixTimerEntryResolver
{
    HotfixTimerEntry<TArgs> ResolveTimerEntry<TCallback, TArgs>(
        Func<TCallback, HotfixTimerCallback<TArgs>> selector)
        where TCallback : class;
}
