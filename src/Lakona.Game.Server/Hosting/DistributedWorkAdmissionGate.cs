using System.Collections.Concurrent;
using System.Threading;

namespace Lakona.Game.Server.Hosting;

internal sealed class DistributedWorkAdmissionGate : IDistributedWorkAdmissionGate
{
    private const long OpenMask = long.MinValue;
    private const long ActiveMask = uint.MaxValue;
    private const int MaximumGeneration = int.MaxValue;

    private readonly object lifecycleGate = new();
    private readonly ConcurrentBag<DistributedWorkAdmissionLease> leases = new();
    private readonly TimeProvider timeProvider;
    private TaskCompletionSource? drainCompletion;
    private long packedState;
    private bool fenced;
    internal MembershipTableSafetyWindow? SafetyWindow { private get; set; }

    public DistributedWorkAdmissionGate(TimeProvider? timeProvider = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsOpen => SafetyWindow?.Check() != false && IsOpenState(Volatile.Read(ref packedState));

    public bool TryEnter(out DistributedWorkAdmission admission)
    {
        if (SafetyWindow?.Check() == false)
        {
            admission = default;
            return false;
        }
        if (!leases.TryTake(out var lease))
        {
            lease = new DistributedWorkAdmissionLease();
        }

        var leaseVersion = lease.Activate();
        while (true)
        {
            var current = Volatile.Read(ref packedState);
            if (!IsOpenState(current))
            {
                CompleteUnusedLease(lease, leaseVersion);
                admission = default;
                return false;
            }

            var active = GetActive(current);
            if (active == uint.MaxValue)
            {
                CompleteUnusedLease(lease, leaseVersion);
                admission = default;
                return false;
            }

            var next = current + 1;
            if (Interlocked.CompareExchange(ref packedState, next, current) == current)
            {
                admission = new DistributedWorkAdmission(
                    GetGeneration(current),
                    lease,
                    leaseVersion);
                if (SafetyWindow?.Check() == false)
                {
                    Exit(admission);
                    admission = default;
                    return false;
                }
                return true;
            }
        }
    }

    public void Exit(DistributedWorkAdmission admission)
    {
        if (!admission.IsAdmitted)
        {
            throw new ArgumentException("An admitted work token is required.", nameof(admission));
        }

        if (!admission.TryComplete())
        {
            throw new InvalidOperationException(
                "The admission token has already been exited.");
        }

        while (true)
        {
            var current = Volatile.Read(ref packedState);
            if (GetGeneration(current) != admission.Generation)
            {
                throw new InvalidOperationException(
                    "The admission token belongs to a different gate generation.");
            }

            var active = GetActive(current);
            if (active == 0)
            {
                throw new InvalidOperationException(
                    "The admission token has already been exited.");
            }

            var next = current - 1;
            if (Interlocked.CompareExchange(ref packedState, next, current) != current)
            {
                continue;
            }

            if (GetActive(next) == 0 && !IsOpenState(next))
            {
                CompleteDrain();
            }


            leases.Add(admission.Lease!);

            return;
        }
    }

    internal void Open()
    {
        SafetyWindow?.Check();
        lock (lifecycleGate)
        {
            if (fenced) throw new InvalidOperationException("Distributed-work admission is permanently fenced.");
            var current = Volatile.Read(ref packedState);
            if (IsOpenState(current))
            {
                throw new InvalidOperationException("Distributed-work admission is already open.");
            }

            if (GetActive(current) != 0)
            {
                throw new InvalidOperationException(
                    "Distributed-work admission cannot open until the previous generation drains.");
            }

            var generation = GetGeneration(current);
            if (generation == MaximumGeneration)
            {
                throw new InvalidOperationException("Distributed-work admission generation is exhausted.");
            }

            drainCompletion = null;
            Volatile.Write(ref packedState, Pack(generation + 1, isOpen: true, active: 0));
        }
    }

    internal async ValueTask<bool> CloseAndDrainAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Drain timeout must be positive.");
        }

        Task? drainTask;
        lock (lifecycleGate)
        {
            Close();

            if (GetActive(Volatile.Read(ref packedState)) == 0)
            {
                return true;
            }

            drainCompletion ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            drainTask = drainCompletion.Task;
        }

        try
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                await drainTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await drainTask.WaitAsync(timeout, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    internal void Fence()
    {
        lock (lifecycleGate)
        {
            fenced = true;
            Close();
        }
    }

    // Called under lifecycleGate, preserving outstanding admission leases.
    private void Close()
    {
        long current;
        do { current = Volatile.Read(ref packedState); }
        while (Interlocked.CompareExchange(ref packedState, current & ~OpenMask, current) != current);
        if (GetActive(current) == 0) drainCompletion?.TrySetResult();
    }

    private void CompleteDrain()
    {
        lock (lifecycleGate)
        {
            var current = Volatile.Read(ref packedState);
            if (!IsOpenState(current) && GetActive(current) == 0)
            {
                drainCompletion?.TrySetResult();
            }
        }
    }

    private void CompleteUnusedLease(
        DistributedWorkAdmissionLease lease,
        long leaseVersion)
    {
        if (!lease.TryComplete(leaseVersion))
        {
            throw new InvalidOperationException(
                "Distributed-work admission lease was completed unexpectedly.");
        }

        leases.Add(lease);
    }

    private static long Pack(int generation, bool isOpen, uint active)
    {
        var state = ((long)generation << 32) | active;
        return isOpen ? state | OpenMask : state;
    }

    private static int GetGeneration(long state)
    {
        return (int)(((ulong)state >> 32) & int.MaxValue);
    }

    private static uint GetActive(long state)
    {
        return (uint)(state & ActiveMask);
    }

    private static bool IsOpenState(long state)
    {
        return (state & OpenMask) != 0;
    }
}
