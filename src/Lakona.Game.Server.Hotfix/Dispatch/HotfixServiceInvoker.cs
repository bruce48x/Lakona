using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixServiceInvoker : IHotfixServiceInvoker
{
    public ValueTask InvokeAsync<TContract, TArg>(
        int methodId,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return HotfixDispatch.Current.InvokeServiceAsync<TContract, TArg>(methodId, arg);
    }

    public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
        int methodId,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return HotfixDispatch.Current.InvokeServiceAsync<TContract, TArg, TResult>(methodId, arg);
    }

    public ValueTask InvokeAsync<TContract, TArg>(
        string methodName,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return HotfixDispatch.Current.InvokeServiceAsync<TContract, TArg>(methodName, arg);
    }

    public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
        string methodName,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return HotfixDispatch.Current.InvokeServiceAsync<TContract, TArg, TResult>(methodName, arg);
    }
}
