using System.Collections.Concurrent;
using System.Threading;

namespace Lakona.Rpc.Transport.Kcp;

internal static class KcpUpdateScheduler
{
    private const int IntervalMs = 10;
    private static readonly ConcurrentDictionary<int, Registration> Registrations = new();
    private static readonly Timer Timer = new(static _ => Tick(), null, IntervalMs, IntervalMs);
    private static int _nextId;
    private static int _tickRunning;

    public static IDisposable Register(Action callback)
    {
        if (callback is null)
            throw new ArgumentNullException(nameof(callback));

        var id = Interlocked.Increment(ref _nextId);
        var registration = new Registration(id, callback);
        Registrations[id] = registration;
        return registration;
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _tickRunning, 1) != 0)
            return;

        try
        {
            foreach (var registration in Registrations.Values)
            {
                registration.Schedule();
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
        private readonly Action _callback;
        private int _disposed;
        private int _running;

        public Registration(int id, Action callback)
        {
            _id = id;
            _callback = callback;
        }

        public void Schedule()
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                return;
            }

            _ = ThreadPool.UnsafeQueueUserWorkItem(
                static state => ((Registration)state!).Run(),
                this);
        }

        private void Run()
        {
            try
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    _callback();
                }
            }
            catch
            {
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Registrations.TryRemove(_id, out _);
        }
    }
}
