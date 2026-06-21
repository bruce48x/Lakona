using Microsoft.Extensions.DependencyInjection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionHotfixLifecycleHandler : IGameSessionLifecycleHandler
{
    private readonly IServiceProvider _services;

    public GameSessionHotfixLifecycleHandler(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
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
        return default;
    }

    public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default)
    {
        var hotfix = _services.GetService<IHotfixServiceInvoker>();
        if (hotfix is null)
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

        return hotfix.InvokeAsync<IGameSessionLifecycle, HotfixLifecycleCall<GameSessionExpiredRequest>>(
            GameSessionLifecycleMethodIds.SessionExpired,
            new HotfixLifecycleCall<GameSessionExpiredRequest>(
                request,
                context.ConnectionId,
                _services,
                _services.GetRequiredService<IActorRuntime>(),
                _services.GetRequiredService<ILakonaGameServer>()),
            cancellationToken);
    }

    public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}
