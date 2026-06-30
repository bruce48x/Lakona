using System.Reflection;
using System.Runtime.ExceptionServices;
using Lakona.Game.Server.Hotfix.Abstractions;
using Lakona.Game.Server.Hotfix.Abstractions.Timers;
using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixFeatureLifecycleInvoker
{
    public async ValueTask StartAsync(
        HotfixFeatureDeclaration feature,
        HotfixFeatureState state,
        IServiceProvider services,
        ILakonaTimerBackend? timerBackend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();

        if (feature.Lifecycle.StartMethod is null)
        {
            return;
        }

        var call = new HotfixFeatureStartCall(feature.Name, state, services, cancellationToken);
        await InvokeAsync(feature.Lifecycle.StartMethod, call, timerBackend).ConfigureAwait(false);
    }

    public async ValueTask StopAsync(
        HotfixFeatureDeclaration feature,
        HotfixFeatureState state,
        IServiceProvider services,
        ILakonaTimerBackend? timerBackend,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(services);
        cancellationToken.ThrowIfCancellationRequested();

        if (feature.Lifecycle.StopMethod is null)
        {
            return;
        }

        var call = new HotfixFeatureStopCall(feature.Name, state, services, cancellationToken);
        await InvokeAsync(feature.Lifecycle.StopMethod, call, timerBackend).ConfigureAwait(false);
    }

    private static async ValueTask InvokeAsync(MethodInfo method, object call, ILakonaTimerBackend? timerBackend)
    {
        var currentScope = HotfixDispatchRuntimeScope.Current;
        using var timerScope = timerBackend is null
            ? HotfixDispatchRuntimeScope.EnterTimerScope()
            : LakonaTimerExecutionScope.Enter(
                timerBackend,
                currentScope?.Lease ?? throw new InvalidOperationException("Feature lifecycle timer dispatch requires an active hotfix runtime scope."));
        object? result;
        try
        {
            result = method.Invoke(null, [call]);
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

        if (result is null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Hotfix feature lifecycle method '{method.DeclaringType?.FullName}.{method.Name}' returned an invalid result.");
    }
}
