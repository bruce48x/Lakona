using System.Reflection;
using System.Runtime.ExceptionServices;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixActorLifecycleInvoker
{
    public async ValueTask StartAsync(
        HotfixDispatchTable table,
        HotfixActorLifecycleDescriptor descriptor,
        object actor,
        object actorId,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();

        if (descriptor.StartMethod is null)
        {
            return;
        }

        using var timerScope = HotfixDispatchRuntimeScope.EnterTimerScope();
        var call = new ActorStartCall(actorId, services, cancellationToken);
        await InvokeAsync(table, descriptor, descriptor.StartMethod, actor, call).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(
        HotfixDispatchTable table,
        HotfixActorLifecycleDescriptor descriptor,
        object actor,
        object actorId,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(services);

        if (descriptor.StopMethod is null)
        {
            return;
        }

        using var timerScope = HotfixDispatchRuntimeScope.EnterTimerScope();
        var call = new ActorStopCall(actorId, services, cancellationToken);
        await InvokeAsync(table, descriptor, descriptor.StopMethod, actor, call).ConfigureAwait(false);
    }

    private static async ValueTask InvokeAsync(
        HotfixDispatchTable table,
        HotfixActorLifecycleDescriptor descriptor,
        MethodInfo method,
        object actor,
        object call)
    {
        object? result;
        try
        {
            result = method.Invoke(table.GetActivatedModule(descriptor.BehaviorType), [actor, call]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        if (result is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException(
            $"Hotfix actor lifecycle method '{method.DeclaringType?.FullName}.{method.Name}' returned an invalid result.");
    }
}
