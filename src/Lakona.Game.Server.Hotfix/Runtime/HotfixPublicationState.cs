using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixPublicationState
{
    public static HotfixPublicationState Empty { get; } = new(
        new HotfixSnapshot(null, null, null, 0, Array.Empty<HotfixMethodKey>(), null, null, null),
        new HotfixRuntimeSnapshot(
            new HotfixServiceInvoker(HotfixDispatch.CurrentFallback),
            EmptyHotfixFeatureCommandInvoker.Instance,
            EmptyServiceProvider.Instance),
        HotfixFeatureLifecycleSnapshot.Empty,
        HotfixDispatch.CurrentFallback);

    public HotfixPublicationState(
        HotfixSnapshot snapshot,
        HotfixRuntimeSnapshot runtime,
        HotfixFeatureLifecycleSnapshot featureLifecycle,
        HotfixDispatchTable dispatchTable)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        FeatureLifecycle = featureLifecycle ?? throw new ArgumentNullException(nameof(featureLifecycle));
        DispatchTable = dispatchTable ?? throw new ArgumentNullException(nameof(dispatchTable));
    }

    public HotfixSnapshot Snapshot { get; }

    public HotfixRuntimeSnapshot Runtime { get; }

    public HotfixFeatureLifecycleSnapshot FeatureLifecycle { get; }

    public HotfixDispatchTable DispatchTable { get; }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
