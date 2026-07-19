using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Actors;
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
        var snapshot = lease.Snapshot;
        var request = new GameSessionDisconnectedRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            ConnectionId = context.ConnectionId
        };

        await snapshot.Invoker.InvokeAsync<IGameSessionLifecycle, HotfixLifecycleCall<GameSessionDisconnectedRequest>>(
            GameSessionLifecycleMethodIds.SessionDisconnected,
            new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
                request,
                context.ConnectionId,
                snapshot.Services,
                snapshot.Services.GetRequiredService<IActorRuntime>(),
                snapshot.Services.GetRequiredService<ILakonaGameServer>()),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        if (_hotfixRuntime is null)
        {
            return;
        }

        using var lease = _hotfixRuntime.AcquireCurrent();
        var snapshot = lease.Snapshot;
        var request = new GameSessionExpiredRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            ConnectionId = context.ConnectionId
        };

        await snapshot.Invoker.InvokeAsync<IGameSessionLifecycle, HotfixLifecycleCall<GameSessionExpiredRequest>>(
            GameSessionLifecycleMethodIds.SessionExpired,
            new HotfixLifecycleCall<GameSessionExpiredRequest>(
                request,
                context.ConnectionId,
                snapshot.Services,
                snapshot.Services.GetRequiredService<IActorRuntime>(),
                snapshot.Services.GetRequiredService<ILakonaGameServer>()),
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}
