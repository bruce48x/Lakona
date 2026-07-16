using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal interface IHotfixActorMethodInvoker
{
    ValueTask<object?> InvokeAsync(
        object behavior,
        object actor,
        object? request,
        CancellationToken cancellationToken);
}

internal static class HotfixActorMethodInvoker
{
    private static readonly MethodInfo CreateResultMethod = typeof(HotfixActorMethodInvoker)
        .GetMethod(nameof(CreateResult), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo CreateNoResultMethod = typeof(HotfixActorMethodInvoker)
        .GetMethod(nameof(CreateNoResult), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static IHotfixActorMethodInvoker Create(
        Type behaviorType,
        Type actorType,
        Type requestType,
        Type? resultType,
        MethodInfo method,
        bool hasCancellationToken)
    {
        var factory = resultType is null
            ? CreateNoResultMethod.MakeGenericMethod(behaviorType, actorType, requestType)
            : CreateResultMethod.MakeGenericMethod(behaviorType, actorType, requestType, resultType);
        return (IHotfixActorMethodInvoker)factory.Invoke(null, [method, hasCancellationToken])!;
    }

    private static IHotfixActorMethodInvoker CreateNoResult<TBehavior, TActor, TRequest>(
        MethodInfo method,
        bool hasCancellationToken)
    {
        return hasCancellationToken
            ? new NoResultInvoker<TBehavior, TActor, TRequest>(
                (Func<TBehavior, TActor, TRequest, CancellationToken, ValueTask>)method.CreateDelegate(
                    typeof(Func<TBehavior, TActor, TRequest, CancellationToken, ValueTask>)))
            : new NoResultInvoker<TBehavior, TActor, TRequest>(
                (Func<TBehavior, TActor, TRequest, ValueTask>)method.CreateDelegate(
                    typeof(Func<TBehavior, TActor, TRequest, ValueTask>)));
    }

    private static IHotfixActorMethodInvoker CreateResult<TBehavior, TActor, TRequest, TResult>(
        MethodInfo method,
        bool hasCancellationToken)
    {
        return hasCancellationToken
            ? new ResultInvoker<TBehavior, TActor, TRequest, TResult>(
                (Func<TBehavior, TActor, TRequest, CancellationToken, ValueTask<TResult>>)method.CreateDelegate(
                    typeof(Func<TBehavior, TActor, TRequest, CancellationToken, ValueTask<TResult>>)))
            : new ResultInvoker<TBehavior, TActor, TRequest, TResult>(
                (Func<TBehavior, TActor, TRequest, ValueTask<TResult>>)method.CreateDelegate(
                    typeof(Func<TBehavior, TActor, TRequest, ValueTask<TResult>>)));
    }

    private sealed class NoResultInvoker<TBehavior, TActor, TRequest> : IHotfixActorMethodInvoker
    {
        private readonly Func<TBehavior, TActor, TRequest, CancellationToken, ValueTask>? withCancellation;
        private readonly Func<TBehavior, TActor, TRequest, ValueTask>? withoutCancellation;

        public NoResultInvoker(Func<TBehavior, TActor, TRequest, CancellationToken, ValueTask> invoker)
        {
            withCancellation = invoker;
        }

        public NoResultInvoker(Func<TBehavior, TActor, TRequest, ValueTask> invoker)
        {
            withoutCancellation = invoker;
        }

        public async ValueTask<object?> InvokeAsync(
            object behavior,
            object actor,
            object? request,
            CancellationToken cancellationToken)
        {
            if (withCancellation is not null)
            {
                await withCancellation((TBehavior)behavior, (TActor)actor, (TRequest)request!, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await withoutCancellation!((TBehavior)behavior, (TActor)actor, (TRequest)request!)
                    .ConfigureAwait(false);
            }

            return null;
        }
    }

    private sealed class ResultInvoker<TBehavior, TActor, TRequest, TResult> : IHotfixActorMethodInvoker
    {
        private readonly Func<TBehavior, TActor, TRequest, CancellationToken, ValueTask<TResult>>? withCancellation;
        private readonly Func<TBehavior, TActor, TRequest, ValueTask<TResult>>? withoutCancellation;

        public ResultInvoker(Func<TBehavior, TActor, TRequest, CancellationToken, ValueTask<TResult>> invoker)
        {
            withCancellation = invoker;
        }

        public ResultInvoker(Func<TBehavior, TActor, TRequest, ValueTask<TResult>> invoker)
        {
            withoutCancellation = invoker;
        }

        public async ValueTask<object?> InvokeAsync(
            object behavior,
            object actor,
            object? request,
            CancellationToken cancellationToken)
        {
            if (withCancellation is not null)
            {
                return await withCancellation((TBehavior)behavior, (TActor)actor, (TRequest)request!, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await withoutCancellation!((TBehavior)behavior, (TActor)actor, (TRequest)request!)
                .ConfigureAwait(false);
        }
    }
}
