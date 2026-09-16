using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixRuntimeSnapshotLease : IDisposable
{
    private HotfixRuntimeSnapshot? _snapshot;
    private readonly IDisposable? _dispatchRuntimeScope;
    private IDisposable? _lifecycleTimerScope;

    internal HotfixRuntimeSnapshotLease(HotfixRuntimeSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _dispatchRuntimeScope = HotfixDispatchRuntimeScope.TryEnter(this);
    }

    public HotfixRuntimeSnapshot Snapshot
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            return snapshot ?? throw new ObjectDisposedException(nameof(HotfixRuntimeSnapshotLease));
        }
    }

    public IHotfixServiceInvoker Invoker => Snapshot.Invoker;

    public IServiceProvider Services => Snapshot.Services;

    /// <summary>Gets the generation's lifecycle instance for direct interface calls.</summary>
    /// <remarks>Keep this lease alive until the callback completes. Do not retain the instance after disposal.</remarks>
    public TContract GetLifecycle<TContract>() where TContract : class
    {
        var snapshot = Snapshot;
        var lifecycle = snapshot.DispatchTable is { } table
            ? table.GetLifecycle<TContract>()
            : snapshot.Services.GetRequiredService<TContract>();
        _lifecycleTimerScope ??= HotfixDispatchRuntimeScope.EnterTimerScope();
        return lifecycle;
    }

    internal IDisposable EnterDispatchScope()
    {
        return HotfixDispatchRuntimeScope.Enter(this);
    }

    public void Dispose()
    {
        _lifecycleTimerScope?.Dispose();
        _dispatchRuntimeScope?.Dispose();
        var snapshot = Interlocked.Exchange(ref _snapshot, null);
        snapshot?.ReleaseLease();
    }
}
