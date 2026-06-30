using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixRuntimeSnapshotLease : IDisposable
{
    private HotfixRuntimeSnapshot? _snapshot;

    internal HotfixRuntimeSnapshotLease(HotfixRuntimeSnapshot snapshot)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
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

    public IHotfixFeatureCommandInvoker FeatureCommands => Snapshot.FeatureCommands;

    public IServiceProvider Services => Snapshot.Services;

    public void Dispose()
    {
        var snapshot = Interlocked.Exchange(ref _snapshot, null);
        snapshot?.ReleaseLease();
    }
}
