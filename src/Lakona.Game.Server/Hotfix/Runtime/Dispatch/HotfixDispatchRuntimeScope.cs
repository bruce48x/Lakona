using Lakona.Game.Server.Hotfix.Timers;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal sealed class HotfixDispatchRuntimeScope : IDisposable
{
    private static readonly AsyncLocal<HotfixDispatchRuntimeContext?> CurrentContext = new();
    private readonly HotfixDispatchRuntimeContext _context;

    private HotfixDispatchRuntimeScope(HotfixRuntimeSnapshotLease lease, ILakonaTimerBackend? timerBackend)
    {
        _context = new HotfixDispatchRuntimeContext(lease, timerBackend, Current);
        CurrentContext.Value = _context;
    }

    internal static HotfixDispatchRuntimeContext? Current => ResolveCurrent(out _);

    internal static HotfixRuntimeSnapshot? CurrentSnapshot
    {
        get
        {
            ResolveCurrent(out var snapshot);
            return snapshot;
        }
    }

    internal static IServiceProvider? CurrentServices => CurrentSnapshot?.Services;

    internal static HotfixDispatchTable? CurrentTable => CurrentSnapshot?.DispatchTable;

    private static HotfixDispatchRuntimeContext? ResolveCurrent(out HotfixRuntimeSnapshot? snapshot)
    {
        // AsyncLocal values are copied between execution contexts, but each frame's
        // lifetime is shared. Another Task may have released one or more top frames.
        for (var context = CurrentContext.Value; context is not null; context = context.Previous)
        {
            if (context.TryGetSnapshot(out var active))
            {
                snapshot = active;
                return context;
            }
        }
        snapshot = null;
        return null;
    }

    internal static HotfixDispatchRuntimeScope? TryEnter(HotfixRuntimeSnapshotLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (lease.Snapshot.DispatchTable is null)
        {
            return null;
        }

        var timerBackend = lease.Snapshot.Services.GetService<ILakonaTimerBackend>();
        return new HotfixDispatchRuntimeScope(lease, timerBackend);
    }

    internal static HotfixDispatchRuntimeScope Enter(HotfixRuntimeSnapshotLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (lease.Snapshot.DispatchTable is null)
        {
            throw new InvalidOperationException("A hotfix dispatch runtime scope requires a dispatch table.");
        }

        var timerBackend = lease.Snapshot.Services.GetService<ILakonaTimerBackend>();
        return new HotfixDispatchRuntimeScope(lease, timerBackend);
    }

    internal static IDisposable? EnterTimerScope()
    {
        var context = ResolveCurrent(out _);
        return context is not { TimerBackend: { } timerBackend }
            ? null
            : LakonaTimerRuntime.Enter(timerBackend, context.Lease);
    }

    public void Dispose()
    {
        if (!_context.Deactivate() || !ReferenceEquals(CurrentContext.Value, _context))
        {
            return;
        }

        CurrentContext.Value = Current;
    }
}

internal sealed class HotfixDispatchRuntimeContext
{
    private int _isActive;

    public HotfixDispatchRuntimeContext(HotfixRuntimeSnapshotLease lease, ILakonaTimerBackend? timerBackend,
        HotfixDispatchRuntimeContext? previous)
    {
        Lease = lease ?? throw new ArgumentNullException(nameof(lease));
        TimerBackend = timerBackend;
        Previous = previous;
        _isActive = 1;
    }

    public HotfixRuntimeSnapshotLease Lease { get; }

    internal HotfixDispatchRuntimeContext? Previous { get; }

    public HotfixRuntimeSnapshot Snapshot => Lease.Snapshot;

    public ILakonaTimerBackend? TimerBackend { get; }

    public bool IsActive => Volatile.Read(ref _isActive) != 0;

    public bool TryGetSnapshot(out HotfixRuntimeSnapshot snapshot)
    {
        snapshot = null!;
        if (Volatile.Read(ref _isActive) == 0)
        {
            return false;
        }

        try
        {
            snapshot = Lease.Snapshot;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        if (Volatile.Read(ref _isActive) != 0)
        {
            return true;
        }

        snapshot = null!;
        return false;
    }

    public bool TryGetServices(out IServiceProvider services)
    {
        if (TryGetSnapshot(out var snapshot))
        {
            services = snapshot.Services;
            return true;
        }

        services = null!;
        return false;
    }

    public bool Deactivate() => Interlocked.Exchange(ref _isActive, 0) != 0;
}
