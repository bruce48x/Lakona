using Lakona.Game.Abstractions;
using Lakona.Game.Abstractions.Sessions;
using Lakona.Game.Server.Sessions;
using Lakona.Rpc.Server;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server;

internal sealed class DefaultLakonaGameServer : ILakonaGameServer
{
    private readonly IGameSessionRegistry _sessions;
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
        GameFrameworkConnectionRegistry connections)
    {
        _sessions = sessions;
        _connectionStates = connectionStates ?? throw new ArgumentNullException(nameof(connectionStates));
        _lifecycleHandlers = lifecycleHandlers?.ToArray() ?? throw new ArgumentNullException(nameof(lifecycleHandlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deliveryPolicies = deliveryPolicies ?? throw new ArgumentNullException(nameof(deliveryPolicies));
        _resumeTickets = resumeTickets ?? throw new ArgumentNullException(nameof(resumeTickets));
        _sessionEstablished = sessionEstablished ?? throw new ArgumentNullException(nameof(sessionEstablished));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
    }

    public async ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default)
    {
        GameSessionKey? session = null;
        try
        {
            session = await _sessions.StartNewSessionAsync(ownerKey, cancellationToken).ConfigureAwait(false);
            await _sessions.SetReliablePushPolicyAsync(session.Value, false, cancellationToken).ConfigureAwait(false);
            return session.Value;
        }
        catch
        {
            if (session is { } created)
            {
                await _sessions.RemoveSessionAsync(created, CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }

    public async ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        GameSessionKey? session = null;
        GameSessionBindResult? binding = null;
        try
        {
            session = await _sessions.StartNewSessionAsync(ownerKey, cancellationToken).ConfigureAwait(false);
            await _sessions.SetReliablePushPolicyAsync(
                session.Value,
                _deliveryPolicies.Get(connectionId),
                cancellationToken).ConfigureAwait(false);
            binding = await PrepareSessionBindingAsync(
                session.Value,
                connectionId,
                cancellationToken).ConfigureAwait(false);
            var resumeTicket = await _resumeTickets
                .IssueAsync(session.Value, _deliveryPolicies.GetEndpointScope(connectionId), cancellationToken)
                .ConfigureAwait(false);
            await _sessionEstablished.NotifyAsync(
                connectionId,
                new Lakona.Game.Abstractions.Sessions.GameSessionEstablished
                {
                    SessionId = session.Value.SessionId,
                    ResumeTicket = resumeTicket,
                },
                cancellationToken).ConfigureAwait(false);
            await CommitSessionBindingAsync(
                session.Value,
                connectionId,
                binding,
                cancellationToken).ConfigureAwait(false);
            return session.Value;
        }
        catch (Exception exception)
        {
            if (session is not { } created)
            {
                throw;
            }

            var cleanupFailures = await RollbackSessionEstablishmentAsync(
                created,
                connectionId,
                binding,
                removeSession: true,
                revokeTicket: true).ConfigureAwait(false);
            if (cleanupFailures.Count == 0)
            {
                throw;
            }

            throw new AggregateException(
                "Game session establishment and rollback failed.",
                [exception, .. cleanupFailures]);
        }
    }

    public async ValueTask BindSessionAsync(
        GameSessionKey session,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        await _sessions.SetReliablePushPolicyAsync(
            session,
            _deliveryPolicies.Get(connectionId),
            cancellationToken).ConfigureAwait(false);
        GameSessionBindResult? binding = null;
        try
        {
            binding = await PrepareSessionBindingAsync(session, connectionId, cancellationToken)
                .ConfigureAwait(false);
            await CommitSessionBindingAsync(
                session,
                connectionId,
                binding,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var cleanupFailures = await RollbackSessionEstablishmentAsync(
                session,
                connectionId,
                binding,
                removeSession: false,
                revokeTicket: false).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        return await _sessions
            .PrepareSessionBindingAsync(session, connectionId, cancellationToken)
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
        bool revokeTicket)
    {
        var failures = new List<Exception>();
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
