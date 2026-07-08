using System.Reflection;
using System.Runtime.ExceptionServices;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

public sealed class HotfixActorLifecycleInvoker
{
    public async ValueTask StartAsync(
        HotfixActorLifecycleDescriptor descriptor,
        object actor,
        object actorId,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();

        if (descriptor.StartMethod is null)
        {
            return;
        }

        var call = new ActorStartCall(actorId, services, cancellationToken);
        await InvokeAsync(descriptor.StartMethod, actor, call).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(
        HotfixActorLifecycleDescriptor descriptor,
        object actor,
        object actorId,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(actorId);
        ArgumentNullException.ThrowIfNull(services);

        if (descriptor.StopMethod is null)
        {
            return;
        }

        var call = new ActorStopCall(actorId, services, cancellationToken);
        await InvokeAsync(descriptor.StopMethod, actor, call).ConfigureAwait(false);
    }

    private static async ValueTask InvokeAsync(
        MethodInfo method,
        object actor,
        object call)
    {
        object? result;
        try
        {
            result = method.Invoke(null, [actor, call]);
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
