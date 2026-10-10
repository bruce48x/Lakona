namespace Lakona.Game.Server.Hosting;

// Owns one renewable deadline, independent of pending membership operations.
internal sealed class MembershipTableSafetyWindow : IDisposable
{
    private readonly object sync = new();
    private readonly TimeProvider time;
    private readonly TimeSpan window;
    private readonly DistributedWorkAdmissionGate admission;
    private readonly Action expired;
    private readonly ITimer timer;
    private long lastContact;
    private bool started;
    private bool fenced;
    private bool disposed;

    public MembershipTableSafetyWindow(TimeProvider time, TimeSpan window,
        DistributedWorkAdmissionGate admission, Action expired)
    {
        this.time = time;
        this.window = window;
        this.admission = admission;
        this.expired = expired;
        timer = time.CreateTimer(_ => CheckOrRenew(renew: false, reschedule: true), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) throw new InvalidOperationException("Membership safety window already started.");
            started = true;
            lastContact = time.GetTimestamp();
            timer.Change(window, Timeout.InfiniteTimeSpan);
        }
    }

    public bool Check() => CheckOrRenew(renew: false);
    public bool Renew() => CheckOrRenew(renew: true);

    private bool CheckOrRenew(bool renew, bool reschedule = false)
    {
        lock (sync)
        {
            if (fenced) return false;
            if (!started || disposed) return true;
            var now = time.GetTimestamp();
            var remaining = window - time.GetElapsedTime(lastContact, now);
            if (remaining > TimeSpan.Zero)
            {
                if (renew)
                {
                    lastContact = now;
                    remaining = window;
                }
                // Also handles an early or stale timer callback after renewal.
                if (renew || reschedule) timer.Change(remaining, Timeout.InfiniteTimeSpan);
                return true;
            }

            fenced = true;
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            admission.Fence();
        }

        // Close admission before invoking cancellation or application callbacks.
        expired();
        return false;
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            timer.Dispose();
        }
    }
}
