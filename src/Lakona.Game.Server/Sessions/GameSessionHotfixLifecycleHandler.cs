using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionHotfixLifecycleHandler : IGameSessionLifecycleHandler
{
    private readonly IHotfixRuntimeAccessor? _hotfixRuntime;
    private readonly GameSessionLifecycleBindings _bindings;

    public GameSessionHotfixLifecycleHandler(GameSessionLifecycleBindings bindings, IHotfixRuntimeAccessor? hotfixRuntime = null)
    {
        _hotfixRuntime = hotfixRuntime;
        _bindings = bindings;
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

        using var lease = _bindings.Acquire(context.Session, _hotfixRuntime, out var lifecycle);
        if (lease is null) return;
        var request = new GameSessionDisconnectedRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            ConnectionId = context.ConnectionId
        };

        cancellationToken.ThrowIfCancellationRequested();
        await lifecycle!.SessionDisconnectedAsync(
            new HotfixLifecycleCall<GameSessionDisconnectedRequest>(request)).ConfigureAwait(false);
    }

    public async ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        if (_hotfixRuntime is null)
        {
            return;
        }

        using var lease = _bindings.Acquire(context.Session, _hotfixRuntime, out var lifecycle);
        if (lease is null) return;
        var request = new GameSessionExpiredRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            ConnectionId = context.ConnectionId
        };

        cancellationToken.ThrowIfCancellationRequested();
        await lifecycle!.SessionExpiredAsync(
            new HotfixLifecycleCall<GameSessionExpiredRequest>(request)).ConfigureAwait(false);
    }

    public async ValueTask OnSessionResumedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        if (_hotfixRuntime is null)
        {
            return;
        }

        using var lease = _bindings.Acquire(context.Session, _hotfixRuntime, out var lifecycle);
        if (lease is null) return;
        var request = new GameSessionResumedRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            ConnectionId = context.ConnectionId
        };

        cancellationToken.ThrowIfCancellationRequested();
        await lifecycle!.SessionResumedAsync(
            new HotfixLifecycleCall<GameSessionResumedRequest>(request)).ConfigureAwait(false);
    }

    public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}
