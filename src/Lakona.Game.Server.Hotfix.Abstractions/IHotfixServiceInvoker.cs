namespace Lakona.Game.Server.Hotfix.Abstractions;

public interface IHotfixServiceInvoker
{
    ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(
        int endpointSlot,
        TArg arg,
        CancellationToken cancellationToken = default);

    ValueTask InvokeAsync<TContract, TArg>(
        int methodId,
        TArg arg,
        CancellationToken cancellationToken = default);

    ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
        int methodId,
        TArg arg,
        CancellationToken cancellationToken = default);
}
