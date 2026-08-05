namespace Lakona.Game.Server.Sessions;

internal sealed class GameSessionResumeTicketTerminationHandler(
    IGameSessionResumeTicketStore tickets) : IGameSessionLifecycleHandler
{
    public ValueTask OnConnectionOpenedAsync(
        GameConnectionContext context,
        CancellationToken cancellationToken = default) => default;

    public ValueTask OnSessionBoundAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default) => default;

    public ValueTask OnSessionDisconnectedAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default) => default;

    public ValueTask OnSessionExpiredAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default) => default;

    public ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default) =>
        context.TerminalOutcomeRetained
            ? default
            : tickets.RevokeAsync(context.Session, cancellationToken);
}
