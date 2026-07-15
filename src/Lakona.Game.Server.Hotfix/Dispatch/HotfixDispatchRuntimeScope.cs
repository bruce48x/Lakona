using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal sealed class HotfixDispatchRuntimeScope : IDisposable
{
    private static readonly AsyncLocal<HotfixDispatchRuntimeContext?> CurrentContext = new();
    private readonly HotfixDispatchRuntimeContext? _previousContext;
    private readonly HotfixDispatchRuntimeContext _context;
    private bool _disposed;

    private HotfixDispatchRuntimeScope(HotfixRuntimeSnapshotLease lease, ILakonaTimerBackend? timerBackend)
    {
        _previousContext = CurrentContext.Value;
        _context = new HotfixDispatchRuntimeContext(lease, timerBackend);
        CurrentContext.Value = _context;
    }

    internal static HotfixDispatchRuntimeContext? Current => CurrentContext.Value;

    internal static IServiceProvider? CurrentServices
    {
        get
        {
            var context = Current;
            return context is not null && context.TryGetServices(out var services)
                ? services
                : null;
        }
    }

    internal static HotfixDispatchTable? CurrentTable
    {
        get
        {
            var context = Current;
            return context is not null && context.TryGetSnapshot(out var snapshot)
                ? snapshot.DispatchTable
                : null;
        }
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
        var context = Current;
        return context is not { TimerBackend: { } timerBackend } || !context.TryGetSnapshot(out _)
            ? null
            : LakonaTimerExecutionScope.Enter(timerBackend, context.Lease);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _context.Deactivate();
        }
        finally
        {
            CurrentContext.Value = _previousContext;
        }
    }
}

internal sealed class HotfixDispatchRuntimeContext
{
    private int _isActive;

    public HotfixDispatchRuntimeContext(HotfixRuntimeSnapshotLease lease, ILakonaTimerBackend? timerBackend)
    {
        Lease = lease ?? throw new ArgumentNullException(nameof(lease));
        TimerBackend = timerBackend;
        _isActive = 1;
    }

    public HotfixRuntimeSnapshotLease Lease { get; }

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

    public void Deactivate()
    {
        Interlocked.Exchange(ref _isActive, 0);
    }
}
