using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal interface IHotfixActorMethodInvoker
{
    ValueTask<object?> InvokeAsync(object behavior, object actor, object? request);
}

internal static class HotfixActorMethodInvoker
{
    public static IHotfixActorMethodInvoker Create(Type behaviorType, Type actorType,
        Type requestType, Type? resultType, MethodInfo method)
    {
        var factory = typeof(HotfixActorMethodInvoker).GetMethod(
            resultType is null ? nameof(CreateNoResult) : nameof(CreateResult),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        factory = resultType is null
            ? factory.MakeGenericMethod(behaviorType, actorType, requestType)
            : factory.MakeGenericMethod(behaviorType, actorType, requestType, resultType);
        return (IHotfixActorMethodInvoker)factory.Invoke(null, [method])!;
    }

    private static IHotfixActorMethodInvoker CreateNoResult<TBehavior, TActor, TRequest>(MethodInfo method) =>
        new NoResultInvoker<TBehavior, TActor, TRequest>(
            (Func<TBehavior, TActor, TRequest, ValueTask>)method.CreateDelegate(
                typeof(Func<TBehavior, TActor, TRequest, ValueTask>)));

    private static IHotfixActorMethodInvoker CreateResult<TBehavior, TActor, TRequest, TResult>(MethodInfo method) =>
        new ResultInvoker<TBehavior, TActor, TRequest, TResult>(
            (Func<TBehavior, TActor, TRequest, ValueTask<TResult>>)method.CreateDelegate(
                typeof(Func<TBehavior, TActor, TRequest, ValueTask<TResult>>)));

    private sealed class NoResultInvoker<TBehavior, TActor, TRequest>(
        Func<TBehavior, TActor, TRequest, ValueTask> invoke) : IHotfixActorMethodInvoker
    {
        public async ValueTask<object?> InvokeAsync(object behavior, object actor, object? request)
        {
            await invoke((TBehavior)behavior, (TActor)actor, (TRequest)request!).ConfigureAwait(false);
            return null;
        }
    }

    private sealed class ResultInvoker<TBehavior, TActor, TRequest, TResult>(
        Func<TBehavior, TActor, TRequest, ValueTask<TResult>> invoke) : IHotfixActorMethodInvoker
    {
        public async ValueTask<object?> InvokeAsync(object behavior, object actor, object? request) =>
            await invoke((TBehavior)behavior, (TActor)actor, (TRequest)request!).ConfigureAwait(false);
    }
}
