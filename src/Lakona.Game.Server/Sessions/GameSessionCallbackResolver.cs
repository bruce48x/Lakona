namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionCallbackResolver
{
    private readonly IGameSessionRegistry sessions;
    private readonly GameFrameworkConnectionRegistry connections;
    private readonly GameSessionCallbackProxyRegistry callbackProxies;

    internal GameSessionCallbackResolver(
        IGameSessionRegistry sessions,
        GameFrameworkConnectionRegistry connections,
        GameSessionCallbackProxyRegistry callbackProxies)
    {
        this.sessions = sessions;
        this.connections = connections;
        this.callbackProxies = callbackProxies;
    }
    public async ValueTask<TCallback?> ResolveAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        return await ResolveAsync(typeof(TCallback), session, cancellationToken).ConfigureAwait(false)
            as TCallback;
    }

    public async ValueTask<object?> ResolveAsync(
        Type callbackContractType,
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callbackContractType);
        var connectionId = await sessions.GetConnectionIdAsync(session, cancellationToken)
            .ConfigureAwait(false);
        if (connectionId is null || connections.Get(connectionId) is not { } connection)
            return null;

        return callbackProxies.TryCreate(callbackContractType, connection);
    }
}
