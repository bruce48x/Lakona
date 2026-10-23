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
        CancellationToken cancellationToken = default) =>
        await StartAsync(table, descriptor, actor, actorId, services, cancellationToken, null).ConfigureAwait(false);

    internal async ValueTask StartAsync(
        HotfixDispatchTable table, HotfixActorLifecycleDescriptor descriptor, object actor,
        object actorId, IServiceProvider services, CancellationToken cancellationToken,
        Lakona.Game.Server.Actors.ActorStartArguments? arguments)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();

        if (descriptor.StartMethod is null)
        {
            if (arguments is not null) throw new InvalidOperationException("Actor start arguments require a typed ActorStart hook.");
            return;
        }

        using var timerScope = HotfixDispatchRuntimeScope.EnterTimerScope();
        var callType = descriptor.StartMethod.GetParameters()[1].ParameterType;
        object call;
        if (callType.IsGenericType && callType.GetGenericTypeDefinition() == typeof(ActorStartCall<>))
        {
            var value = arguments?.Decode(callType.GenericTypeArguments[0]);
            call = Activator.CreateInstance(callType, actorId, services, value, arguments is not null, cancellationToken)!;
        }
        else
        {
            if (arguments is not null) throw new InvalidOperationException("This ActorStart does not accept arguments.");
            call = new ActorStartCall(actorId, services, cancellationToken);
        }
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
