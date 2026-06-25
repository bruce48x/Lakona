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

    public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = _hotfixRuntime?.Current;
        if (snapshot is null)
        {
            return default;
        }

        var request = new GameSessionDisconnectedRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            Generation = context.Session.Generation,
            ConnectionId = context.ConnectionId,
            CallbackContractTypeNames = context.CallbackContractTypes
                .Select(static type => type.FullName ?? type.Name)
                .ToList()
        };

        return snapshot.Invoker.InvokeAsync<IGameSessionLifecycle, HotfixLifecycleCall<GameSessionDisconnectedRequest>>(
            GameSessionLifecycleMethodIds.SessionDisconnected,
            new HotfixLifecycleCall<GameSessionDisconnectedRequest>(
                request,
                context.ConnectionId,
                snapshot.Services,
                snapshot.Services.GetRequiredService<IActorRuntime>(),
                snapshot.Services.GetRequiredService<ILakonaGameServer>()),
            cancellationToken);
    }

    public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = _hotfixRuntime?.Current;
        if (snapshot is null)
        {
            return default;
        }

        var request = new GameSessionExpiredRequest
        {
            OwnerKey = context.Session.OwnerKey,
            SessionId = context.Session.SessionId,
            Generation = context.Session.Generation,
            ConnectionId = context.ConnectionId,
            CallbackContractTypeNames = context.CallbackContractTypes
                .Select(static type => type.FullName ?? type.Name)
                .ToList()
        };

        return snapshot.Invoker.InvokeAsync<IGameSessionLifecycle, HotfixLifecycleCall<GameSessionExpiredRequest>>(
            GameSessionLifecycleMethodIds.SessionExpired,
            new HotfixLifecycleCall<GameSessionExpiredRequest>(
                request,
                context.ConnectionId,
                snapshot.Services,
                snapshot.Services.GetRequiredService<IActorRuntime>(),
                snapshot.Services.GetRequiredService<ILakonaGameServer>()),
            cancellationToken);
    }

    public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}
