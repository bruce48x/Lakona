namespace Lakona.Game.Server.Hosting;

public abstract class LakonaRpcServiceBinder
{
    public abstract void Bind(LakonaGameServerRpcContext context);

    public virtual bool TryCreateCallback(
        Type callbackContractType,
        Lakona.Rpc.Server.RpcSession session,
        out object? callback)
    {
        callback = null;
        return false;
    }
}
