using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server;

internal sealed class DefaultLakonaGameServer : ILakonaGameServer
{
    private readonly IGameSessionRegistry _sessions;
    private readonly IGameSessionResumeService _resume;
    private readonly IClientSessionRouteRegistrar _clientSessionRoutes;
    private readonly IGameSessionConnectionCloser _connectionCloser;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _lifecycleHandlers;
    private readonly ILogger<DefaultLakonaGameServer> _logger;

    public DefaultLakonaGameServer(
        IGameSessionRegistry sessions,
        IGameSessionResumeService resume,
        IClientSessionRouteRegistrar clientSessionRoutes,
        IGameSessionConnectionCloser connectionCloser,
        IEnumerable<IGameSessionLifecycleHandler> lifecycleHandlers)
        : this(
            sessions,
            resume,
            clientSessionRoutes,
            connectionCloser,
            lifecycleHandlers,
            NullLogger<DefaultLakonaGameServer>.Instance)
    {
    }

    public DefaultLakonaGameServer(
        IGameSessionRegistry sessions,
        IGameSessionResumeService resume,
        IClientSessionRouteRegistrar clientSessionRoutes,
        IGameSessionConnectionCloser connectionCloser,
        IEnumerable<IGameSessionLifecycleHandler> lifecycleHandlers,
        ILogger<DefaultLakonaGameServer> logger)
    {
        _sessions = sessions;
        _resume = resume;
        _clientSessionRoutes = clientSessionRoutes ?? throw new ArgumentNullException(nameof(clientSessionRoutes));
        _connectionCloser = connectionCloser;
        _lifecycleHandlers = lifecycleHandlers?.ToArray() ?? throw new ArgumentNullException(nameof(lifecycleHandlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default)
    {
        return _sessions.StartNewSessionAsync(ownerKey, cancellationToken);
    }

    public async ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
        string ownerKey,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        var session = await StartSessionAsync(ownerKey, cancellationToken).ConfigureAwait(false);
        await BindSessionAsync(session, connectionId, callback, cancellationToken)
            .ConfigureAwait(false);
        return session;
    }

    public async ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
        GameSessionResumeRequest request,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        var decision = await _resume.TryResumeAsync(request, cancellationToken).ConfigureAwait(false);
        if (decision.Session is { } session &&
            decision.Status is SessionResumeStatus.Resumed or SessionResumeStatus.StateRefreshRequired)
        {
            await BindSessionAsync(session, connectionId, callback, cancellationToken)
                .ConfigureAwait(false);
        }

        return decision;
    }

    public async ValueTask BindSessionAsync<TCallback>(
        GameSessionKey session,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        var result = await _sessions.BindSessionAsync(
            session,
            connectionId,
            callback,
            cancellationToken).ConfigureAwait(false);

        if (result.SessionBecameActive is { } snapshot)
        {
            await _clientSessionRoutes.RegisterAsync(snapshot.Session, cancellationToken).ConfigureAwait(false);
            await PublishSessionBoundAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask BindCurrentSessionAsync<TCallback>(
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        var result = await _sessions.BindCurrentSessionAsync(
            connectionId,
            callback,
            cancellationToken).ConfigureAwait(false);

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

    public ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        return _sessions.GetCallbackAsync<TCallback>(session, cancellationToken);
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

        var binding = await _sessions
            .GetSessionBindingAsync<ILakonaGameSessionCallback>(session, cancellationToken)
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

        if (binding is null)
        {
            return;
        }

        await TryNotifySessionTerminatedAsync(
                binding.Callback,
                notice,
                options.NotifyTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        await _connectionCloser
            .CloseConnectionAsync(session, binding.ConnectionId, notice, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask PublishSessionBoundAsync(
        GameSessionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var context = new GameSessionBindingContext(
            snapshot.Session,
            snapshot.ConnectionId,
            snapshot.CallbackContractTypes);
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
