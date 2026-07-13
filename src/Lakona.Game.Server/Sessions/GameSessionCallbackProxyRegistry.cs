using Lakona.Game.Server.Hosting;
using Lakona.Rpc.Server;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionCallbackProxyRegistry
{
    private readonly Lock _gate = new();
    private readonly List<LakonaRpcServiceBinder> _binders = [];

    public void Add(LakonaRpcServiceBinder binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        lock (_gate) _binders.Add(binder);
    }

    public object Create(Type callbackContractType, RpcSession session)
    {
        LakonaRpcServiceBinder[] binders;
        lock (_gate) binders = [.. _binders];
        foreach (var binder in binders)
        {
            if (binder.TryCreateCallback(callbackContractType, session, out var callback) && callback is not null)
                return callback;
        }

        throw new InvalidOperationException(
            $"No RPC callback proxy is registered for '{callbackContractType.FullName}'.");
    }

    public object? TryCreate(Type callbackContractType, RpcSession session)
    {
        LakonaRpcServiceBinder[] binders;
        lock (_gate) binders = [.. _binders];
        foreach (var binder in binders)
        {
            if (binder.TryCreateCallback(callbackContractType, session, out var callback) && callback is not null)
                return callback;
        }

        return null;
    }
}
