using System.Reflection;

namespace Lakona.Game.Server.Hotfix.Dispatch;

internal interface IHotfixActorMethodInvoker
{
    ValueTask<object?> InvokeAsync(
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
        Type actorType,
        Type requestType,
        Type? resultType,
        MethodInfo method,
        bool hasCancellationToken)
    {
        var factory = resultType is null
            ? CreateNoResultMethod.MakeGenericMethod(actorType, requestType)
            : CreateResultMethod.MakeGenericMethod(actorType, requestType, resultType);
        return (IHotfixActorMethodInvoker)factory.Invoke(null, [method, hasCancellationToken])!;
    }

    private static IHotfixActorMethodInvoker CreateNoResult<TActor, TRequest>(
        MethodInfo method,
        bool hasCancellationToken)
    {
        return hasCancellationToken
            ? new NoResultInvoker<TActor, TRequest>(
                (Func<TActor, TRequest, CancellationToken, ValueTask>)method.CreateDelegate(
                    typeof(Func<TActor, TRequest, CancellationToken, ValueTask>)))
            : new NoResultInvoker<TActor, TRequest>(
                (Func<TActor, TRequest, ValueTask>)method.CreateDelegate(
                    typeof(Func<TActor, TRequest, ValueTask>)));
    }

    private static IHotfixActorMethodInvoker CreateResult<TActor, TRequest, TResult>(
        MethodInfo method,
        bool hasCancellationToken)
    {
        return hasCancellationToken
            ? new ResultInvoker<TActor, TRequest, TResult>(
                (Func<TActor, TRequest, CancellationToken, ValueTask<TResult>>)method.CreateDelegate(
                    typeof(Func<TActor, TRequest, CancellationToken, ValueTask<TResult>>)))
            : new ResultInvoker<TActor, TRequest, TResult>(
                (Func<TActor, TRequest, ValueTask<TResult>>)method.CreateDelegate(
                    typeof(Func<TActor, TRequest, ValueTask<TResult>>)));
    }

    private sealed class NoResultInvoker<TActor, TRequest> : IHotfixActorMethodInvoker
    {
        private readonly Func<TActor, TRequest, CancellationToken, ValueTask>? withCancellation;
        private readonly Func<TActor, TRequest, ValueTask>? withoutCancellation;

        public NoResultInvoker(Func<TActor, TRequest, CancellationToken, ValueTask> invoker)
        {
            withCancellation = invoker;
        }

        public NoResultInvoker(Func<TActor, TRequest, ValueTask> invoker)
        {
            withoutCancellation = invoker;
        }

        public async ValueTask<object?> InvokeAsync(
            object actor,
            object? request,
            CancellationToken cancellationToken)
        {
            if (withCancellation is not null)
            {
                await withCancellation((TActor)actor, (TRequest)request!, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await withoutCancellation!((TActor)actor, (TRequest)request!).ConfigureAwait(false);
            }

            return null;
        }
    }

    private sealed class ResultInvoker<TActor, TRequest, TResult> : IHotfixActorMethodInvoker
    {
        private readonly Func<TActor, TRequest, CancellationToken, ValueTask<TResult>>? withCancellation;
        private readonly Func<TActor, TRequest, ValueTask<TResult>>? withoutCancellation;

        public ResultInvoker(Func<TActor, TRequest, CancellationToken, ValueTask<TResult>> invoker)
        {
            withCancellation = invoker;
        }

        public ResultInvoker(Func<TActor, TRequest, ValueTask<TResult>> invoker)
        {
            withoutCancellation = invoker;
        }

        public async ValueTask<object?> InvokeAsync(
            object actor,
            object? request,
            CancellationToken cancellationToken)
        {
            if (withCancellation is not null)
            {
                return await withCancellation((TActor)actor, (TRequest)request!, cancellationToken).ConfigureAwait(false);
            }

            return await withoutCancellation!((TActor)actor, (TRequest)request!).ConfigureAwait(false);
        }
    }
}
