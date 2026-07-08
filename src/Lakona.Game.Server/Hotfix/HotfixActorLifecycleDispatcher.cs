using Lakona.Game.Server.Actors;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixActorLifecycleDispatcher(IServiceProvider serviceProvider) : IActorLifecycleDispatcher
{
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

        using var lease = runtimeAccessor.AcquireCurrent();
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

        using var lease = runtimeAccessor.AcquireCurrent();
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
}
