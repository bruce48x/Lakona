using System.Collections.Concurrent;
using System.Threading;

namespace Lakona.Rpc.Transport.Kcp;

internal interface IKcpUpdateRegistration : IDisposable
{
    void Reschedule(DateTimeOffset nextUpdate);
}

internal static class KcpUpdateScheduler
{
    private const int IntervalMs = 10;
    private static readonly ConcurrentDictionary<int, Registration> Registrations = new();
    private static readonly Timer Timer = new(static _ => Tick(), null, IntervalMs, IntervalMs);
    private static int _nextId;
    private static int _tickRunning;

    public static IKcpUpdateRegistration Register(
        Func<DateTimeOffset, DateTimeOffset> callback,
        Action<Exception> onFault)
    {
        if (callback is null)
            throw new ArgumentNullException(nameof(callback));
        if (onFault is null)
            throw new ArgumentNullException(nameof(onFault));

        var id = Interlocked.Increment(ref _nextId);
        var registration = new Registration(id, callback, onFault);
        Registrations[id] = registration;
        return registration;
    }

    private static void Tick()
    {
        if (Interlocked.Exchange(ref _tickRunning, 1) != 0)
            return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var registration in Registrations.Values)
            {
                registration.Schedule(now);
            }
        }
        finally
        {
            Volatile.Write(ref _tickRunning, 0);
        }
    }

    private sealed class Registration : IKcpUpdateRegistration
    {
        private readonly int _id;
        private readonly Func<DateTimeOffset, DateTimeOffset> _callback;
        private readonly Action<Exception> _onFault;
        private readonly object _deadlineGate = new();
        private long _nextUpdateUtcTicks;
        private long _deadlineVersion;
        private int _disposed;
        private int _running;

        public Registration(
            int id,
            Func<DateTimeOffset, DateTimeOffset> callback,
            Action<Exception> onFault)
        {
            _id = id;
            _callback = callback;
            _onFault = onFault;
        }

        public void Schedule(DateTimeOffset now)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                now.UtcDateTime.Ticks < Volatile.Read(ref _nextUpdateUtcTicks) ||
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
                    long deadlineVersion;
                    lock (_deadlineGate)
                    {
                        deadlineVersion = _deadlineVersion;
                    }

                    var now = DateTimeOffset.UtcNow;
                    var nextUpdate = _callback(now);
                    lock (_deadlineGate)
                    {
                        // I/O may replace the deadline while this callback is running.
                        if (_deadlineVersion == deadlineVersion)
                        {
                            Volatile.Write(ref _nextUpdateUtcTicks, nextUpdate.UtcDateTime.Ticks);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Registrations.TryRemove(_id, out _);
                    _onFault(exception);
                }
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        }

        public void Reschedule(DateTimeOffset nextUpdate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            lock (_deadlineGate)
            {
                _deadlineVersion++;
                Volatile.Write(ref _nextUpdateUtcTicks, nextUpdate.UtcDateTime.Ticks);
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
