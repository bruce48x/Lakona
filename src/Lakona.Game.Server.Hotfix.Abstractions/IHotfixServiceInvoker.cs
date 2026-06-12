namespace Lakona.Game.Server.Hotfix.Abstractions;

public interface IHotfixServiceInvoker
{
    ValueTask InvokeAsync<TContract, TArg>(
        string methodName,
        TArg arg,
        CancellationToken cancellationToken = default);

    ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
        string methodName,
        TArg arg,
        CancellationToken cancellationToken = default);
}
