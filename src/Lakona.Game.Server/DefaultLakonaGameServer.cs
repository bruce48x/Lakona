using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server;

internal sealed class DefaultLakonaGameServer : ILakonaGameServer
{
    private readonly IGameSessionRegistry _sessions;
    private readonly IGameSessionResumeService _resume;
    private readonly IClientSessionRouteRegistrar _clientSessionRoutes;
    private readonly IGameSessionConnectionCloser _connectionCloser;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _lifecycleHandlers;
    private readonly ILogger<DefaultLakonaGameServer> _logger;
    private readonly GameConnectionDeliveryPolicyRegistry _deliveryPolicies;
    private readonly IGameSessionResumeTicketStore _resumeTickets;
    private readonly IGameSessionEstablishedNotifier _sessionEstablished;
    private readonly GameSessionCallbackResolver _callbackResolver;

    public DefaultLakonaGameServer(
        IGameSessionRegistry sessions,
        IGameSessionResumeService resume,
        IClientSessionRouteRegistrar clientSessionRoutes,
        IGameSessionConnectionCloser connectionCloser,
        IEnumerable<IGameSessionLifecycleHandler> lifecycleHandlers,
        ILogger<DefaultLakonaGameServer> logger,
        GameConnectionDeliveryPolicyRegistry deliveryPolicies,
        IGameSessionResumeTicketStore resumeTickets,
        IGameSessionEstablishedNotifier sessionEstablished,
        GameSessionCallbackResolver callbackResolver)
    {
        _sessions = sessions;
        _resume = resume;
        _clientSessionRoutes = clientSessionRoutes ?? throw new ArgumentNullException(nameof(clientSessionRoutes));
        _connectionCloser = connectionCloser;
        _lifecycleHandlers = lifecycleHandlers?.ToArray() ?? throw new ArgumentNullException(nameof(lifecycleHandlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deliveryPolicies = deliveryPolicies ?? throw new ArgumentNullException(nameof(deliveryPolicies));
        _resumeTickets = resumeTickets ?? throw new ArgumentNullException(nameof(resumeTickets));
        _sessionEstablished = sessionEstablished ?? throw new ArgumentNullException(nameof(sessionEstablished));
        _callbackResolver = callbackResolver ?? throw new ArgumentNullException(nameof(callbackResolver));
    }

    public async ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.StartNewSessionAsync(ownerKey, cancellationToken).ConfigureAwait(false);
        await _sessions.SetReliablePushPolicyAsync(session, false, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessions.StartNewSessionAsync(ownerKey, cancellationToken).ConfigureAwait(false);
        await _sessions.SetReliablePushPolicyAsync(
            session,
            _deliveryPolicies.Get(connectionId),
            cancellationToken).ConfigureAwait(false);
        await BindSessionAsync(session, connectionId, cancellationToken)
            .ConfigureAwait(false);
        var resumeTicket = await _resumeTickets
            .IssueAsync(session, _deliveryPolicies.GetEndpointScope(connectionId), cancellationToken)
            .ConfigureAwait(false);
        await _sessionEstablished.NotifyAsync(
            connectionId,
            new Lakona.Game.Abstractions.Sessions.GameSessionEstablished
            {
                SessionId = session.SessionId,
                ResumeTicket = resumeTicket,
            },
            cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async ValueTask<SessionResumeDecision> ResumeSessionAsync(
        GameSessionResumeRequest request,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        var decision = await _resume.TryResumeAsync(request, cancellationToken).ConfigureAwait(false);
        if (decision.Session is { } session &&
            decision.Status is SessionResumeStatus.Resumed or SessionResumeStatus.StateRefreshRequired)
        {
            await BindSessionAsync(session, connectionId, cancellationToken).ConfigureAwait(false);
        }

        return decision;
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
        var result = await _sessions.BindSessionAsync(session, connectionId, cancellationToken)
            .ConfigureAwait(false);

        if (result.SessionBecameActive is { } snapshot)
        {
            await _clientSessionRoutes.RegisterAsync(snapshot.Session, cancellationToken).ConfigureAwait(false);
            await PublishSessionBoundAsync(snapshot, cancellationToken).ConfigureAwait(false);
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

        var connectionId = await _sessions.GetConnectionIdAsync(session, cancellationToken)
            .ConfigureAwait(false);
        var callback = await _callbackResolver
            .ResolveAsync<ILakonaGameSessionCallback>(session, cancellationToken)
            .ConfigureAwait(false);
        var notice = new SessionTerminationNotice(reason, message);

        await _sessions
            .MarkSessionTerminatedAsync(
                session,
                notice,
                options.KeepTerminalStateForResume,
                cancellationToken)
            .ConfigureAwait(false);

        await PublishSessionTerminatedAsync(session, notice, cancellationToken).ConfigureAwait(false);

        if (connectionId is null)
        {
            return;
        }

        if (callback is not null)
        {
            await TryNotifySessionTerminatedAsync(
                    callback,
                    notice,
                    options.NotifyTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _connectionCloser
            .CloseConnectionAsync(session, connectionId, notice, cancellationToken)
            .ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        var context = new GameSessionTerminationContext(session, notice);
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

    private static async ValueTask TryNotifySessionTerminatedAsync(
        ILakonaGameSessionCallback callback,
        SessionTerminationNotice notice,
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
            await callback
                .OnSessionTerminatedAsync(notice, timeoutCts.Token)
                .AsTask()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        catch (TimeoutException)
        {
        }
        catch
        {
        }
    }
}
