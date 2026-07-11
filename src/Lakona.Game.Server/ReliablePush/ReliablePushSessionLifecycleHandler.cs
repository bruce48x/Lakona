using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server.ReliablePush;

internal sealed class ReliablePushSessionLifecycleHandler : IGameSessionLifecycleHandler
{
    private readonly IReliablePushOutbox outbox;

    public ReliablePushSessionLifecycleHandler(IReliablePushOutbox outbox)
    {
        this.outbox = outbox;
    }

    public ValueTask OnConnectionOpenedAsync(
        GameConnectionContext context,
        CancellationToken cancellationToken = default) => default;

    public ValueTask OnSessionBoundAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default) => default;

    public ValueTask OnSessionDisconnectedAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default) => default;

    public ValueTask OnSessionTerminatedAsync(
        GameSessionTerminationContext context,
        CancellationToken cancellationToken = default) =>
        outbox.RemoveAsync(context.Session, cancellationToken);

    public ValueTask OnSessionExpiredAsync(
        GameSessionBindingContext context,
        CancellationToken cancellationToken = default) =>
        outbox.RemoveAsync(context.Session, cancellationToken);
}
