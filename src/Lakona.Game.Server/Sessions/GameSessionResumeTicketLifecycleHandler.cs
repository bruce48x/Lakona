namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionResumeTicketLifecycleHandler(
    IGameSessionResumeTicketStore tickets) : IGameSessionLifecycleHandler
{
    public ValueTask OnConnectionOpenedAsync(GameConnectionContext context, CancellationToken cancellationToken = default) => default;
    public ValueTask OnSessionBoundAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default) => default;
    public ValueTask OnSessionDisconnectedAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default) => default;
    public ValueTask OnSessionExpiredAsync(GameSessionBindingContext context, CancellationToken cancellationToken = default) =>
        tickets.RevokeAsync(context.Session, cancellationToken);
    public ValueTask OnSessionTerminatedAsync(GameSessionTerminationContext context, CancellationToken cancellationToken = default) =>
        tickets.RevokeAsync(context.Session, cancellationToken);
}
