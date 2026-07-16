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
    ClientNotificationTarget<TCallback> ForSession<TCallback>(GameSessionKey session)
        where TCallback : class;
}

/// <summary>
/// Sends typed callback notifications to one selected game session.
/// </summary>
public readonly struct ClientNotificationTarget<TCallback>
    where TCallback : class
{
    private readonly IClientNotificationCommandRouter? _router;
    private readonly GameSessionKey _session;

    internal ClientNotificationTarget(
        IClientNotificationCommandRouter router,
        GameSessionKey session)
    {
        _router = router;
        _session = session;
    }

    /// <summary>
    /// Dispatches one source-generated client notification command.
    /// </summary>
    /// <typeparam name="TPayload">The notification DTO type.</typeparam>
    /// <param name="serviceId">The stable RPC service id.</param>
    /// <param name="methodId">The stable notification method id.</param>
    /// <param name="methodName">The notification method name used by legacy local callbacks.</param>
    /// <param name="payload">The notification DTO.</param>
    /// <param name="cancellationToken">A token that cancels notification admission before the framework accepts it.</param>
    /// <returns>The framework admission status. Actual delivery runs asynchronously after acceptance.</returns>
    /// <remarks>
    /// The callback contract type must match a callback bound through
    /// <see cref="ILakonaGameServer"/>. When reliable push is enabled, the same
    /// call may be sequenced and replayed by the framework before the client ack
    /// closes the notification. Once accepted, caller cancellation no longer
    /// cancels the framework-owned delivery attempt.
    /// </remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public ValueTask<ClientNotificationStatus> DispatchGeneratedAsync<TPayload>(
        int serviceId,
        int methodId,
        string methodName,
        TPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (_router is null)
        {
            throw new InvalidOperationException("The notification target was not created by IClientNotifications.");
        }

        return _router.DispatchGeneratedAsync<TCallback, TPayload>(
            _session,
            serviceId,
            methodId,
            methodName,
            payload,
            cancellationToken);
    }
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
