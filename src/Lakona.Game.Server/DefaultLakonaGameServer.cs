using Lakona.Game.Abstractions;
using Lakona.Game.Server.Hotfix;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server;

internal sealed class DefaultLakonaGameServer : ILakonaGameServer
{
    private readonly IGameSessionRegistry _sessions;
    private readonly GameSessionLifecycleBindings _sessionLifecycles;
    private readonly GameHandshakeConnectionStateRegistry _connectionStates;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _lifecycleHandlers;
    private readonly ILogger<DefaultLakonaGameServer> _logger;
    private readonly GameConnectionDeliveryPolicyRegistry _deliveryPolicies;
    private readonly IGameSessionResumeTicketStore _resumeTickets;
    private readonly IGameSessionEstablishedNotifier _sessionEstablished;
    private readonly GameFrameworkConnectionRegistry _connections;

    public DefaultLakonaGameServer(
        IGameSessionRegistry sessions,
        GameHandshakeConnectionStateRegistry connectionStates,
        IEnumerable<IGameSessionLifecycleHandler> lifecycleHandlers,
        ILogger<DefaultLakonaGameServer> logger,
        GameConnectionDeliveryPolicyRegistry deliveryPolicies,
        IGameSessionResumeTicketStore resumeTickets,
        IGameSessionEstablishedNotifier sessionEstablished,
        GameFrameworkConnectionRegistry connections,
        GameSessionLifecycleBindings sessionLifecycles)
    {
        _sessions = sessions;
        _sessionLifecycles = sessionLifecycles;
        _connectionStates = connectionStates ?? throw new ArgumentNullException(nameof(connectionStates));
        _lifecycleHandlers = lifecycleHandlers?.ToArray() ?? throw new ArgumentNullException(nameof(lifecycleHandlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deliveryPolicies = deliveryPolicies ?? throw new ArgumentNullException(nameof(deliveryPolicies));
        _resumeTickets = resumeTickets ?? throw new ArgumentNullException(nameof(resumeTickets));
        _sessionEstablished = sessionEstablished ?? throw new ArgumentNullException(nameof(sessionEstablished));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public ValueTask<GameSessionKey> StartSessionAsync(string ownerKey, CancellationToken cancellationToken = default)
        => StartUnboundSessionAsync(ownerKey, null, cancellationToken);

    public ValueTask<GameSessionKey> StartSessionAsync(string ownerKey, string connectionId, CancellationToken cancellationToken = default)
        => StartBoundSessionAsync(ownerKey, connectionId, null, cancellationToken);

    public ValueTask<GameSessionKey> StartSessionAsync<TLifecycle>(string ownerKey, CancellationToken cancellationToken = default)
        where TLifecycle : class, IGameSessionLifecycle
        => StartUnboundSessionAsync(ownerKey, GameSessionLifecycleBindings.Identity(typeof(TLifecycle)), cancellationToken);

    public ValueTask<GameSessionKey> StartSessionAsync<TLifecycle>(string ownerKey, string connectionId, CancellationToken cancellationToken = default)
        where TLifecycle : class, IGameSessionLifecycle
        => StartBoundSessionAsync(ownerKey, connectionId, GameSessionLifecycleBindings.Identity(typeof(TLifecycle)), cancellationToken);

    private ValueTask<GameSessionKey> StartUnboundSessionAsync(
        string ownerKey, string? lifecycleIdentity, CancellationToken cancellationToken)
    {
        if (_sessions is not IGameSessionEndpointBindingRegistry)
            throw new NotSupportedException(
                "Creating a session before its connection is known requires IGameSessionEndpointBindingRegistry.");
        return CreateSessionAsync(ownerKey, lifecycleIdentity, cancellationToken);
    }

    private async ValueTask<GameSessionKey> CreateSessionAsync(
        string ownerKey, string? lifecycleIdentity, CancellationToken cancellationToken)
    {
        var session = await _sessions.StartNewSessionAsync(ownerKey, cancellationToken).ConfigureAwait(false);
        try
        {
            if (lifecycleIdentity is not null) _sessionLifecycles.Bind(session, lifecycleIdentity);
            return session;
        }
        catch (Exception exception)
        {
            _sessionLifecycles.Remove(session);
            try
            {
                await _sessions.RemoveSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Game session creation and rollback failed.", exception, cleanupFailure);
            }
            throw;
        }
    }

    private async ValueTask<GameSessionKey> StartBoundSessionAsync(
        string ownerKey, string connectionId, string? lifecycleIdentity, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        var session = await CreateSessionAsync(ownerKey, lifecycleIdentity, cancellationToken).ConfigureAwait(false);
        await BindConnectionAsync(session, connectionId, newSession: true, resumeTicket: null, cancellationToken)
            .ConfigureAwait(false);
        return session;
    }

    public async ValueTask BindSessionAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        await BindConnectionAsync(session, connectionId, newSession: false, resumeTicket: null, cancellationToken)
            .ConfigureAwait(false);
    }

    internal ValueTask<bool> TryBindRecoveredSessionAsync(
        GameSessionKey session, string connectionId, string resumeTicket, CancellationToken cancellationToken) =>
        BindConnectionAsync(session, connectionId, newSession: false, resumeTicket, cancellationToken);

    private async ValueTask<bool> BindConnectionAsync(
        GameSessionKey session, string connectionId, bool newSession, string? resumeTicket, CancellationToken cancellationToken)
    {
        GameSessionBindResult? binding = null;
        var issuingTicket = false;
        string? provisionalTicket = null;
        try
        {
            var policy = _deliveryPolicies.GetBindingPolicy(connectionId);
            binding = await PrepareSessionBindingAsync(session, connectionId, policy, cancellationToken)
                .ConfigureAwait(false);
            if (resumeTicket is not null)
            {
                // A provisional ticket may have been revoked between the handshake lookup and this reservation.
                if (binding.IsFirstBinding ||
                    await _resumeTickets.ResolveAsync(resumeTicket, policy.EndpointScope, cancellationToken)
                        .ConfigureAwait(false) != session)
                {
                    await _sessions.RollbackSessionBindingAsync(session, connectionId, CancellationToken.None)
                        .ConfigureAwait(false);
                    return false;
                }
            }
            else if (newSession || binding.IsFirstBinding)
            {
                issuingTicket = true;
                provisionalTicket = await _resumeTickets.IssueAsync(session, policy.EndpointScope, cancellationToken)
                    .ConfigureAwait(false);
                await _sessionEstablished.NotifyAsync(connectionId, new GameSessionEstablished
                {
                    SessionId = session.SessionId,
                    ResumeTicket = provisionalTicket
                }, cancellationToken).ConfigureAwait(false);
            }

            await CommitSessionBindingAsync(session, connectionId, binding, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception)
        {
            var cleanupFailures = await RollbackSessionEstablishmentAsync(
                session,
                connectionId,
                binding,
                removeSession: newSession,
                revokeTicket: issuingTicket,
                closeConnection: provisionalTicket is not null).ConfigureAwait(false);
            if (cleanupFailures.Count == 0)
            {
                throw;
            }

            throw new AggregateException(
                "Game session binding and rollback failed.",
                [exception, .. cleanupFailures]);
        }
    }

    public ValueTask MarkSessionDisconnectedAsync(
        GameSessionKey session,
        string? connectionId = null,
        CancellationToken cancellationToken = default)
    {
        return _sessions.MarkSessionDisconnectedAsync(session, connectionId, cancellationToken);
    }

    public ValueTask SetSessionItemAsync(
        GameSessionKey session,
        string key,
        GameSessionItemValue value,
        CancellationToken cancellationToken = default)
    {
        return _sessions.SetSessionItemAsync(session, key, value, cancellationToken);
    }

    public ValueTask<GameSessionItemValue?> GetSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default)
    {
        return _sessions.GetSessionItemAsync(session, key, cancellationToken);
    }

    public ValueTask<GameSessionItems> GetSessionItemsAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
    {
        return _sessions.GetSessionItemsAsync(session, cancellationToken);
    }

    public ValueTask RemoveSessionItemAsync(
        GameSessionKey session,
        string key,
        CancellationToken cancellationToken = default)
    {
        return _sessions.RemoveSessionItemAsync(session, key, cancellationToken);
    }

    public async ValueTask TerminateSessionAsync(
        GameSessionKey session,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SessionTerminationOptions();
        cancellationToken.ThrowIfCancellationRequested();

        var notice = new SessionTerminationNotice(reason, message);

        var terminatedBinding = await _sessions
            .MarkSessionTerminatedAsync(
                session,
                notice,
                options.KeepTerminalStateForResume,
                cancellationToken)
            .ConfigureAwait(false);
        _sessionLifecycles.Remove(session);
        var connectionId = terminatedBinding?.ConnectionId;
        var notifications = connectionId is null ? null : _connections.Get(connectionId);

        await PublishSessionTerminatedAsync(
            session,
            notice,
            options.KeepTerminalStateForResume,
            CancellationToken.None).ConfigureAwait(false);

        if (connectionId is null)
        {
            return;
        }

        if (notifications is not null)
        {
            await TryNotifySessionTerminatedAsync(
                    notifications,
                    notice,
                    connectionId,
                    options.NotifyTimeout,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }

        _connectionStates.TryClose(connectionId);
    }

    private async ValueTask<GameSessionBindResult> PrepareSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        GameConnectionDeliveryPolicyRegistry.DeliveryPolicy policy,
        CancellationToken cancellationToken)
    {
        if (_sessions is IGameSessionEndpointBindingRegistry registry)
            return await registry.PrepareEndpointSessionBindingAsync(
                session, connectionId, policy.ReliablePush, policy.SerializerProfile, cancellationToken).ConfigureAwait(false);

        // Registries implementing the original API still support immediate establishment and subsequent rebinds.
        await _sessions.SetReliablePushPolicyAsync(session, policy.ReliablePush, cancellationToken).ConfigureAwait(false);
        return policy.SerializerProfile is null
            ? await _sessions.PrepareSessionBindingAsync(session, connectionId, cancellationToken).ConfigureAwait(false)
            : await _sessions.PrepareProfiledSessionBindingAsync(session, connectionId, policy.SerializerProfile, cancellationToken)
                .ConfigureAwait(false);
    }

    private async ValueTask CommitSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        GameSessionBindResult binding,
        CancellationToken cancellationToken)
    {
        await _sessions
            .CommitSessionBindingAsync(session, connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (binding.SessionBecameActive is { } snapshot)
        {
            await PublishSessionBoundAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async ValueTask<IReadOnlyList<Exception>> RollbackSessionEstablishmentAsync(
        GameSessionKey session,
        string connectionId,
        GameSessionBindResult? binding,
        bool removeSession,
        bool revokeTicket,
        bool closeConnection)
    {
        var failures = new List<Exception>();
        if (removeSession) _sessionLifecycles.Remove(session);
        if (closeConnection)
        {
            // An establishment ACK identifies only its connection. Retire it before another attempt can wait for an ACK.
            await TryRollbackStepAsync(() =>
            {
                _connections.Remove(connectionId);
                _connectionStates.TryClose(connectionId);
                return default;
            }, "establishment connection", failures).ConfigureAwait(false);
        }
        if (revokeTicket)
        {
            await TryRollbackStepAsync(
                () => _resumeTickets.RevokeAsync(session, CancellationToken.None),
                "resume ticket",
                failures).ConfigureAwait(false);
        }

        if (binding is not null)
        {
            await TryRollbackStepAsync(
                () => _sessions.RollbackSessionBindingAsync(
                    session,
                    connectionId,
                    CancellationToken.None),
                "session binding",
                failures).ConfigureAwait(false);
        }

        if (removeSession)
        {
            await TryRollbackStepAsync(
                () => _sessions.RemoveSessionAsync(session, CancellationToken.None),
                "new session",
                failures).ConfigureAwait(false);
        }

        return failures;
    }

    private async ValueTask TryRollbackStepAsync(
        Func<ValueTask> rollback,
        string step,
        ICollection<Exception> failures)
    {
        try
        {
            await rollback().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            _logger.LogError(
                exception,
                "Failed to roll back {RollbackStep} for game session establishment.",
                step);
        }
    }

    private async ValueTask PublishSessionBoundAsync(
        GameSessionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var context = new GameSessionBindingContext(
            snapshot.Session,
            snapshot.ConnectionId);
        foreach (var handler in _lifecycleHandlers)
        {
            try
            {
                await handler.OnSessionBoundAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Game session-bound lifecycle handler failed for {ConnectionId}.",
                    snapshot.ConnectionId);
            }
        }
    }

    private async ValueTask PublishSessionTerminatedAsync(
        GameSessionKey session,
        SessionTerminationNotice notice,
        bool terminalOutcomeRetained,
        CancellationToken cancellationToken)
    {
        var context = new GameSessionTerminationContext(
            session,
            notice,
            terminalOutcomeRetained);
        foreach (var handler in _lifecycleHandlers)
        {
            try
            {
                await handler.OnSessionTerminatedAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Game session terminated lifecycle handler failed for owner {OwnerKey}.",
                    session.OwnerKey);
        }
    }
    }

    private async ValueTask TryNotifySessionTerminatedAsync(
        RpcNotificationChannel notifications,
        SessionTerminationNotice notice,
        string connectionId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            var payload = LakonaInternalCodec.EncodeSessionTerminationNotice(notice);
            await notifications
                .SendRawAsync(
                    GameSessionNotificationRpcIds.ServiceId,
                    GameSessionNotificationRpcIds.TerminatedNotificationId,
                    payload,
                    cancellationToken: timeoutCts.Token)
                .AsTask()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Timed out notifying terminated game session for connection {ConnectionId} after {NotifyTimeout}.",
                connectionId,
                timeout);
        }
        catch (TimeoutException)
        {
            _logger.LogDebug(
                "Timed out notifying terminated game session for connection {ConnectionId} after {NotifyTimeout}.",
                connectionId,
                timeout);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to notify terminated game session for connection {ConnectionId}.",
                connectionId);
        }
    }
}
