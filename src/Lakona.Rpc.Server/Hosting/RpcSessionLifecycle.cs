namespace Lakona.Rpc.Server;

public sealed record RpcSessionLifecycleContext(
    string ConnectionId,
    string DisplayName);

public interface IRpcSessionLifecycleObserver
{
    ValueTask OnSessionStartedAsync(
        RpcSessionLifecycleContext context,
        CancellationToken cancellationToken = default);

    ValueTask OnSessionDisconnectedAsync(
        RpcSessionLifecycleContext context,
        Exception? error,
        CancellationToken cancellationToken = default);
}
