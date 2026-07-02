namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Creates notification targets for server-to-client callback delivery.
/// </summary>
/// <remarks>
/// Business code publishes callback intent through this API. The framework
/// decides whether delivery uses reliable push, best-effort local dispatch, or
/// cluster routing for the target session.
/// </remarks>
public interface IClientNotifications
{
    /// <summary>
    /// Selects a game session as the target for a notification.
    /// </summary>
    /// <param name="session">The framework session key that should receive the notification.</param>
    /// <returns>A notification target scoped to <paramref name="session"/>.</returns>
    IClientNotificationTarget ForSession(GameSessionKey session);
}

/// <summary>
/// Sends typed callback notifications to one selected game session.
/// </summary>
public interface IClientNotificationTarget
{
    /// <summary>
    /// Invokes a typed client callback for the selected session.
    /// </summary>
    /// <typeparam name="TCallback">The typed callback contract implemented by the client.</typeparam>
    /// <param name="notify">The callback invocation to perform.</param>
    /// <param name="cancellationToken">A token that cancels notification dispatch before delivery completes.</param>
    /// <returns>The delivery status reported by the notification pipeline.</returns>
    /// <remarks>
    /// The callback contract type must match a callback bound through
    /// <see cref="ILakonaGameServer"/>. When reliable push is enabled, the same
    /// call may be sequenced and replayed by the framework before the client ack
    /// closes the notification.
    /// </remarks>
    ValueTask<ClientNotificationStatus> NotifyAsync<TCallback>(
        Func<TCallback, ValueTask> notify,
        CancellationToken cancellationToken = default)
        where TCallback : class;
}

/// <summary>
/// Receives typed notification payloads from framework notification adapters.
/// </summary>
/// <typeparam name="TPayload">The notification payload type.</typeparam>
public interface IClientNotificationSink<in TPayload>
{
    /// <summary>
    /// Handles one notification payload.
    /// </summary>
    /// <param name="payload">The payload to deliver.</param>
    /// <param name="cancellationToken">A token that cancels payload handling.</param>
    ValueTask OnNotificationAsync(TPayload payload, CancellationToken cancellationToken = default);
}
