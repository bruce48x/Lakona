using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix.Dispatch;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixActorLifecycleDispatcher(IServiceProvider serviceProvider) : IActorLifecycleDispatcher
{
    public bool HasStartHook(Type actorType)
    {
        return TryResolveDescriptor(actorType, out var descriptor) && descriptor.StartMethodName is not null;
    }

    public bool HasStopHook(Type actorType)
    {
        return TryResolveDescriptor(actorType, out var descriptor) && descriptor.StopMethodName is not null;
    }

    public async ValueTask StartAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeAccessor = serviceProvider.GetService<IHotfixRuntimeAccessor>();
        if (runtimeAccessor is null)
        {
            return;
        }

        HotfixRuntimeSnapshotLease lease;
        try
        {
            lease = runtimeAccessor.AcquireCurrent();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        using var _ = lease;
        var snapshot = lease.Snapshot;
        if (snapshot.DispatchTable is null ||
            !snapshot.DispatchTable.TryResolveActorLifecycle(actorType, out var descriptor))
        {
            return;
        }

        var invoker = serviceProvider.GetRequiredService<HotfixActorLifecycleInvoker>();
        await invoker
            .StartAsync(descriptor, actor, actorId, snapshot.Services, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask StopAsync(
        Type actorType,
        ActorId actorId,
        object actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(actor);

        var runtimeAccessor = serviceProvider.GetService<IHotfixRuntimeAccessor>();
        if (runtimeAccessor is null)
        {
            return;
        }

        HotfixRuntimeSnapshotLease lease;
        try
        {
            lease = runtimeAccessor.AcquireCurrent();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        using var _ = lease;
        var snapshot = lease.Snapshot;
        if (snapshot.DispatchTable is null ||
            !snapshot.DispatchTable.TryResolveActorLifecycle(actorType, out var descriptor))
        {
            return;
        }

        var invoker = serviceProvider.GetRequiredService<HotfixActorLifecycleInvoker>();
        await invoker
            .StopAsync(descriptor, actor, actorId, snapshot.Services, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool TryResolveDescriptor(
        Type actorType,
        out HotfixActorLifecycleDescriptor descriptor)
    {
        descriptor = null!;
        ArgumentNullException.ThrowIfNull(actorType);

        var runtimeAccessor = serviceProvider.GetService<IHotfixRuntimeAccessor>();
        if (runtimeAccessor is null)
        {
            return false;
        }

        HotfixRuntimeSnapshotLease lease;
        try
        {
            lease = runtimeAccessor.AcquireCurrent();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        using var _ = lease;
        return lease.Snapshot.DispatchTable is not null &&
            lease.Snapshot.DispatchTable.TryResolveActorLifecycle(actorType, out descriptor);
    }
}
