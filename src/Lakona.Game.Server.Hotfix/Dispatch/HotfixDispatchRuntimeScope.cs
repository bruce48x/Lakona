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

    internal static HotfixDispatchTable? CurrentTable
    {
        get
        {
            var context = Current;
            return context is { IsActive: true }
                ? context.Snapshot.DispatchTable
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

    internal static IDisposable? EnterTimerScope()
    {
        var context = Current;
        return context is not { IsActive: true, TimerBackend: { } timerBackend }
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
    public HotfixDispatchRuntimeContext(HotfixRuntimeSnapshotLease lease, ILakonaTimerBackend? timerBackend)
    {
        Lease = lease ?? throw new ArgumentNullException(nameof(lease));
        TimerBackend = timerBackend;
        IsActive = true;
    }

    public HotfixRuntimeSnapshotLease Lease { get; }

    public HotfixRuntimeSnapshot Snapshot => Lease.Snapshot;

    public ILakonaTimerBackend? TimerBackend { get; }

    public bool IsActive { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
    }
}
