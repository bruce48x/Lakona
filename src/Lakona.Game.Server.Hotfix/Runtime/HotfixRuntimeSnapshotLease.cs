using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixRuntimeSnapshotLease : IDisposable, IHotfixTimerEntryResolver
{
    private HotfixRuntimeSnapshot? _snapshot;
    private readonly IDisposable? _dispatchRuntimeScope;

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

    internal IDisposable EnterDispatchScope()
    {
        return HotfixDispatchRuntimeScope.Enter(this);
    }

    HotfixTimerEntry<TArgs> IHotfixTimerEntryResolver.ResolveTimerEntry<TCallback, TArgs>(
        Func<TCallback, HotfixTimerCallback<TArgs>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var table = Snapshot.DispatchTable
            ?? throw new InvalidOperationException("The active hotfix generation has no dispatch table.");
        return table.ResolveTimerEntry(selector);
    }

    public void Dispose()
    {
        _dispatchRuntimeScope?.Dispose();
        var snapshot = Interlocked.Exchange(ref _snapshot, null);
        snapshot?.ReleaseLease();
    }
}
