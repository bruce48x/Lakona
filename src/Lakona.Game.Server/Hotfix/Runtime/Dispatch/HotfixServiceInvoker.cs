using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixServiceInvoker : IHotfixServiceInvoker
{
    private readonly Func<HotfixDispatchTable> _current;

    public HotfixServiceInvoker()
        : this(static () => HotfixDispatch.ActiveTable)
    {
    }

    internal HotfixServiceInvoker(HotfixDispatchTable table)
        : this(() => table)
    {
        ArgumentNullException.ThrowIfNull(table);
    }

    private HotfixServiceInvoker(Func<HotfixDispatchTable> current)
    {
        _current = current;
    }

    public async ValueTask<TResult> InvokeHttpAsync<TArg, TResult>(
        int endpointSlot,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timerScope = HotfixDispatchRuntimeScope.EnterTimerScope();
        return await _current()
            .InvokeHttpAsync<TArg, TResult>(endpointSlot, arg)
            .ConfigureAwait(false);
    }

    public async ValueTask InvokeAsync<TContract, TArg>(
        int methodId,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timerScope = HotfixDispatchRuntimeScope.EnterTimerScope();
        await _current().InvokeServiceAsync<TContract, TArg>(methodId, arg).ConfigureAwait(false);
    }

    public async ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
        int methodId,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timerScope = HotfixDispatchRuntimeScope.EnterTimerScope();
        return await _current().InvokeServiceAsync<TContract, TArg, TResult>(methodId, arg).ConfigureAwait(false);
    }
}
