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
        await StartCoreAsync(actorType, actorId, actor, null, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask StartAsync(Type actorType, ActorId actorId, object actor,
        ActorStartArguments arguments, CancellationToken cancellationToken = default) =>
        StartCoreAsync(actorType, actorId, actor, arguments, cancellationToken);

    private async ValueTask StartCoreAsync(Type actorType, ActorId actorId, object actor,
        ActorStartArguments? arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actorType);
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();

        var runtimeAccessor = serviceProvider.GetService<IHotfixRuntimeAccessor>();
        if (runtimeAccessor is null)
        {
            if (arguments is not null) throw new InvalidOperationException("The Actor startup runtime is unavailable.");
            return;
        }

        HotfixRuntimeSnapshotLease lease;
        try
        {
            lease = runtimeAccessor.AcquireCurrent();
        }
        catch (ObjectDisposedException)
        {
            if (arguments is not null) throw;
            return;
        }

        using var _ = lease;
        var snapshot = lease.Snapshot;
        if (snapshot.DispatchTable is null ||
            !snapshot.DispatchTable.TryResolveActorLifecycle(actorType, out var descriptor))
        {
            if (arguments is not null) throw new InvalidOperationException("Actor start arguments require a typed ActorStart hook.");
            return;
        }

        var invoker = serviceProvider.GetRequiredService<HotfixActorLifecycleInvoker>();
        await invoker
            .StartAsync(snapshot.DispatchTable, descriptor, actor, actorId, snapshot.Services, cancellationToken, arguments)
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
            .StopAsync(snapshot.DispatchTable, descriptor, actor, actorId, snapshot.Services, cancellationToken)
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
