using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Dispatch;

public sealed class HotfixServiceInvoker : IHotfixServiceInvoker
{
    private readonly Func<HotfixDispatchTable> _current;

    public HotfixServiceInvoker()
        : this(static () => HotfixDispatch.Current)
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

    public ValueTask InvokeAsync<TContract, TArg>(
        int methodId,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _current().InvokeServiceAsync<TContract, TArg>(methodId, arg);
    }

    public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
        int methodId,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _current().InvokeServiceAsync<TContract, TArg, TResult>(methodId, arg);
    }

    public ValueTask InvokeAsync<TContract, TArg>(
        string methodName,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _current().InvokeServiceAsync<TContract, TArg>(methodName, arg);
    }

    public ValueTask<TResult> InvokeAsync<TContract, TArg, TResult>(
        string methodName,
        TArg arg,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _current().InvokeServiceAsync<TContract, TArg, TResult>(methodName, arg);
    }
}
