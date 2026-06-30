using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal sealed class HotfixDispatchRuntimeScope : IDisposable
{
    private static readonly AsyncLocal<HotfixDispatchRuntimeContext?> CurrentContext = new();
    private readonly HotfixDispatchRuntimeContext? _previousContext;
    private bool _disposed;

    private HotfixDispatchRuntimeScope(HotfixRuntimeSnapshotLease lease, ILakonaTimerBackend? timerBackend)
    {
        _previousContext = CurrentContext.Value;
        CurrentContext.Value = new HotfixDispatchRuntimeContext(lease, timerBackend);
    }

    internal static HotfixDispatchRuntimeContext? Current => CurrentContext.Value;

    internal static HotfixDispatchTable? CurrentTable => Current?.Snapshot.DispatchTable;

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
        return context?.TimerBackend is null
            ? null
            : LakonaTimerExecutionScope.Enter(context.TimerBackend, context.Lease);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CurrentContext.Value = _previousContext;
    }
}

internal sealed class HotfixDispatchRuntimeContext
{
    public HotfixDispatchRuntimeContext(HotfixRuntimeSnapshotLease lease, ILakonaTimerBackend? timerBackend)
    {
        Lease = lease ?? throw new ArgumentNullException(nameof(lease));
        TimerBackend = timerBackend;
    }

    public HotfixRuntimeSnapshotLease Lease { get; }

    public HotfixRuntimeSnapshot Snapshot => Lease.Snapshot;

    public ILakonaTimerBackend? TimerBackend { get; }
}
