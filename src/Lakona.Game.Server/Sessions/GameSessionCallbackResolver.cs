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
        var callback = await ResolveAsync(typeof(TCallback), session, cancellationToken).ConfigureAwait(false)
            as TCallback;
        return callback ?? await sessions.GetCallbackAsync<TCallback>(session, cancellationToken)
            .ConfigureAwait(false);
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
            return await ResolveLegacyAsync(callbackContractType, session, cancellationToken).ConfigureAwait(false);

        return callbackProxies.TryCreate(callbackContractType, connection);
    }

    private async ValueTask<object?> ResolveLegacyAsync(
        Type callbackContractType,
        GameSessionKey session,
        CancellationToken cancellationToken)
    {
        var method = typeof(IGameSessionRegistry)
            .GetMethod(nameof(IGameSessionRegistry.GetCallbackAsync))!
            .MakeGenericMethod(callbackContractType);
        var valueTask = method.Invoke(sessions, [session, cancellationToken]);
        if (valueTask is null) return null;
        var task = (Task)valueTask.GetType().GetMethod("AsTask")!.Invoke(valueTask, null)!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
}
