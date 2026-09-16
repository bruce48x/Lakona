using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionHotfixLifecycleHandler : IGameSessionLifecycleHandler
{
    private readonly IHotfixRuntimeAccessor? _hotfixRuntime;

    public GameSessionHotfixLifecycleHandler(IHotfixRuntimeAccessor? hotfixRuntime = null)
    {
        _hotfixRuntime = hotfixRuntime;
    }

    public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }

    public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }

    public async ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        if (_hotfixRuntime is null)
        {
            return;
        }

        using var lease = _hotfixRuntime.AcquireCurrent();
        var request = new GameSessionDisconnectedRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            ConnectionId = context.ConnectionId
        };

        cancellationToken.ThrowIfCancellationRequested();
        await lease.GetLifecycle<IGameSessionLifecycle>().SessionDisconnectedAsync(
            new HotfixLifecycleCall<GameSessionDisconnectedRequest>(request)).ConfigureAwait(false);
    }

    public async ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        if (_hotfixRuntime is null)
        {
            return;
        }

        using var lease = _hotfixRuntime.AcquireCurrent();
        var request = new GameSessionExpiredRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            ConnectionId = context.ConnectionId
        };

        cancellationToken.ThrowIfCancellationRequested();
        await lease.GetLifecycle<IGameSessionLifecycle>().SessionExpiredAsync(
            new HotfixLifecycleCall<GameSessionExpiredRequest>(request)).ConfigureAwait(false);
    }

    public async ValueTask OnSessionResumedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        if (_hotfixRuntime is null)
        {
            return;
        }

        using var lease = _hotfixRuntime.AcquireCurrent();
        var request = new GameSessionResumedRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            ConnectionId = context.ConnectionId
        };

        cancellationToken.ThrowIfCancellationRequested();
        await lease.GetLifecycle<IGameSessionLifecycle>().SessionResumedAsync(
            new HotfixLifecycleCall<GameSessionResumedRequest>(request)).ConfigureAwait(false);
    }

    public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}
