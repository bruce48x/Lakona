using System.Collections.Concurrent;

namespace Lakona.Game.Server.Internal.ActorKernel.Core;

internal sealed class ActorTimerSet
{
    private readonly ConcurrentBag<IDisposable> timers = new();
    private int disposed;

    internal void Add(IDisposable timer)
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            timer.Dispose();
            return;
        }

        timers.Add(timer);

        if (Volatile.Read(ref disposed) != 0)
        {
            timer.Dispose();
        }
    }

    internal void DisposeAll()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (IDisposable timer in timers)
        {
            timer.Dispose();
        }
    }
}
