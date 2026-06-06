using System.Collections.Concurrent;
using System.Threading;

namespace Lakona.Rpc.Transport.Kcp;

internal static class KcpUpdateScheduler
{
    private const int IntervalMs = 10;
    private static readonly ConcurrentDictionary<int, Action> Callbacks = new();
    private static readonly Timer Timer = new(static _ => Tick(), null, IntervalMs, IntervalMs);
    private static int _nextId;
    private static int _tickRunning;

    public static IDisposable Register(Action callback)
    {
        if (callback is null)
            throw new ArgumentNullException(nameof(callback));

        var id = Interlocked.Increment(ref _nextId);
        Callbacks[id] = callback;
        return new Registration(id);
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _tickRunning, 1) != 0)
            return;

        try
        {
            foreach (var callback in Callbacks.Values)
            {
                try
                {
                    callback();
                }
                catch
                {
                }
            }
        }
        finally
        {
            Volatile.Write(ref _tickRunning, 0);
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly int _id;
        private int _disposed;

        public Registration(int id)
        {
            _id = id;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Callbacks.TryRemove(_id, out _);
        }
    }
}
