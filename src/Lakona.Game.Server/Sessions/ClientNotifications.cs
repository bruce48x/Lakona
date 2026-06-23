using Lakona.Game.Server.ReliablePush;

namespace Lakona.Game.Server.Sessions;

public sealed class ClientNotifications : IClientNotifications
{
    private const string ControlSessionKind = "control";

    private readonly IClientSessionIndex _sessions;
    private readonly IGameSessionRegistry _directory;
    private readonly IReliablePushOutbox _reliablePush;
    private readonly ReliablePushOptions _reliablePushOptions;

    public ClientNotifications(
        IClientSessionIndex sessions,
        IGameSessionRegistry directory,
        IReliablePushOutbox reliablePush,
        ReliablePushOptions reliablePushOptions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _reliablePush = reliablePush ?? throw new ArgumentNullException(nameof(reliablePush));
        _reliablePushOptions = reliablePushOptions ?? throw new ArgumentNullException(nameof(reliablePushOptions));
    }

    public IClientNotificationTarget ForUser(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new Target(this, userId);
    }

    private async ValueTask PublishAsync<TPayload>(
        string userId,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var current = await _sessions.FindCurrentAsync(userId, ControlSessionKind, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return;
        }

        var sink = await _directory
            .GetCallbackAsync<IClientNotificationSink<TPayload>>(current.Session, cancellationToken)
            .ConfigureAwait(false);
        if (sink is null)
        {
            return;
        }

        await sink.OnNotificationAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishReliableAsync<TPayload>(
        string userId,
        string kind,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        if (!_reliablePushOptions.Enabled)
        {
            await PublishAsync(userId, payload, cancellationToken).ConfigureAwait(false);
            return;
        }

        var current = await _sessions.FindCurrentAsync(userId, ControlSessionKind, cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return;
        }

        await _reliablePush.PublishAsync(
            ReliablePushSessionOwnerKey.Create(current.Session),
            kind,
            payload!,
            record => PublishAsync(userId, (TPayload)record.Payload, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class Target(ClientNotifications owner, string userId) : IClientNotificationTarget
    {
        public ValueTask PublishAsync<TPayload>(
            TPayload payload,
            CancellationToken cancellationToken = default)
        {
            return owner.PublishAsync(userId, payload, cancellationToken);
        }

        public ValueTask PublishReliableAsync<TPayload>(
            string kind,
            TPayload payload,
            CancellationToken cancellationToken = default)
        {
            return owner.PublishReliableAsync(userId, kind, payload, cancellationToken);
        }
    }
}
