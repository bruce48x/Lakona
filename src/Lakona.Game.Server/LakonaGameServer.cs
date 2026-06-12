using Lakona.Game.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lakona.Game.Server;

public sealed class LakonaGameServer : ILakonaGameServer
{
    private readonly IGameSessionDirectory _sessions;
    private readonly IGameSessionResumeService _resume;
    private readonly IReliablePushOutbox _reliablePush;
    private readonly IReliablePushAckService _reliablePushAcks;
    private readonly IGameSessionEndpointCloser _endpointCloser;
    private readonly IReadOnlyList<IGameSessionLifecycleHandler> _lifecycleHandlers;
    private readonly ILogger<LakonaGameServer> _logger;

    public LakonaGameServer(
        IGameSessionDirectory sessions,
        IGameSessionResumeService resume,
        IReliablePushOutbox reliablePush,
        IReliablePushAckService reliablePushAcks,
        IGameSessionEndpointCloser endpointCloser,
        IEnumerable<IGameSessionLifecycleHandler> lifecycleHandlers)
        : this(
            sessions,
            resume,
            reliablePush,
            reliablePushAcks,
            endpointCloser,
            lifecycleHandlers,
            NullLogger<LakonaGameServer>.Instance)
    {
    }

    public LakonaGameServer(
        IGameSessionDirectory sessions,
        IGameSessionResumeService resume,
        IReliablePushOutbox reliablePush,
        IReliablePushAckService reliablePushAcks,
        IGameSessionEndpointCloser endpointCloser,
        IEnumerable<IGameSessionLifecycleHandler> lifecycleHandlers,
        ILogger<LakonaGameServer> logger)
    {
        _sessions = sessions;
        _resume = resume;
        _reliablePush = reliablePush;
        _reliablePushAcks = reliablePushAcks;
        _endpointCloser = endpointCloser;
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
        GameEndpointName endpointName,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        var session = await StartSessionAsync(ownerKey, cancellationToken).ConfigureAwait(false);
        await BindEndpointAsync(session, endpointName, connectionId, callback, cancellationToken)
            .ConfigureAwait(false);
        return session;
    }

    public async ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
        GameSessionResumeRequest request,
        GameEndpointName endpointName,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        var decision = await _resume.TryResumeAsync(request, cancellationToken).ConfigureAwait(false);
        if (decision.Session is { } session &&
            decision.Status is SessionResumeStatus.Resumed or SessionResumeStatus.StateRefreshRequired)
        {
            await BindEndpointAsync(session, endpointName, connectionId, callback, cancellationToken)
                .ConfigureAwait(false);
        }

        return decision;
    }

    public async ValueTask BindEndpointAsync<TCallback>(
        GameSessionKey session,
        GameEndpointName endpointName,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        var result = await _sessions.BindEndpointAsync(
            new SessionEndpointKey(session, endpointName),
            connectionId,
            callback,
            cancellationToken).ConfigureAwait(false);

        if (result.EndpointBecameActive is { } snapshot)
        {
            await PublishEndpointBoundAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask MarkEndpointDisconnectedAsync(
        GameSessionKey session,
        GameEndpointName endpointName,
        string? connectionId = null,
        CancellationToken cancellationToken = default)
    {
        return _sessions.MarkEndpointDisconnectedAsync(
            new SessionEndpointKey(session, endpointName),
            connectionId,
            cancellationToken);
    }

    public ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        GameEndpointName endpointName,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        return _sessions.GetCallbackAsync<TCallback>(
            new SessionEndpointKey(session, endpointName),
            cancellationToken);
    }

    public ValueTask TerminateSessionAsync(
        GameSessionKey session,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return TerminateSessionAsync(
            session,
            GameEndpointName.Control,
            reason,
            message,
            options,
            cancellationToken);
    }

    public async ValueTask TerminateSessionAsync(
        GameSessionKey session,
        GameEndpointName endpointName,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SessionTerminationOptions();
        cancellationToken.ThrowIfCancellationRequested();

        var endpoint = new SessionEndpointKey(session, endpointName);
        var binding = await _sessions
            .GetEndpointBindingAsync<ILakonaGameSessionCallback>(endpoint, cancellationToken)
            .ConfigureAwait(false);
        var notice = new SessionTerminationNotice(session, reason, message);

        await _sessions
            .MarkSessionTerminatedAsync(
                session,
                notice,
                options.KeepTerminalStateForResume,
                cancellationToken)
            .ConfigureAwait(false);

        await PublishSessionTerminatedAsync(notice, cancellationToken).ConfigureAwait(false);

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

        await _endpointCloser
            .CloseEndpointAsync(endpoint, binding.ConnectionId, notice, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<long> PublishReliablePushAsync<TCallback, TPayload>(
        GameSessionKey session,
        GameEndpointName endpointName,
        string kind,
        TPayload payload,
        ReliablePushDeliver<TCallback, TPayload> deliver,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(deliver);

        return _reliablePush.PublishAsync(
            session,
            kind,
            payload ?? throw new ArgumentNullException(nameof(payload)),
            record => DeliverReliablePushRecordAsync(session, endpointName, kind, record, deliver, cancellationToken),
            cancellationToken);
    }

    public ValueTask<long> PublishReliablePushAsync(
        GameSessionKey session,
        string kind,
        object payload,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default)
    {
        return _reliablePush.PublishAsync(session, kind, payload, deliver, cancellationToken);
    }

    public ValueTask ReplayReliablePushAsync(
        GameSessionKey session,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default)
    {
        return _reliablePush.ReplayPendingAsync(session, deliver, cancellationToken);
    }

    public ValueTask ReplayReliablePushAsync<TCallback, TPayload>(
        GameSessionKey session,
        GameEndpointName endpointName,
        string kind,
        ReliablePushDeliver<TCallback, TPayload> deliver,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        ArgumentNullException.ThrowIfNull(deliver);

        return _reliablePush.ReplayPendingAsync(
            session,
            record => DeliverReliablePushRecordAsync(session, endpointName, kind, record, deliver, cancellationToken),
            cancellationToken);
    }

    public ValueTask<ReliablePushAckOutcome> AckReliablePushAsync(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        return _reliablePushAcks.AckAsync(
            currentSession,
            acknowledgedSession,
            sequence,
            cancellationToken);
    }

    private async ValueTask DeliverReliablePushRecordAsync<TCallback, TPayload>(
        GameSessionKey session,
        GameEndpointName endpointName,
        string kind,
        ReliablePushRecord record,
        ReliablePushDeliver<TCallback, TPayload> deliver,
        CancellationToken cancellationToken)
        where TCallback : class
    {
        if (!string.Equals(record.Kind, kind, StringComparison.Ordinal))
        {
            return;
        }

        if (record.Payload is not TPayload payload)
        {
            throw new InvalidOperationException(
                $"Reliable push record '{record.Kind}' payload is not assignable to '{typeof(TPayload).FullName}'.");
        }

        var callback = await GetCallbackAsync<TCallback>(session, endpointName, cancellationToken)
            .ConfigureAwait(false);
        if (callback is null)
        {
            return;
        }

        await deliver(
            callback,
            ReliablePushSequence.From(record.Sequence),
            payload,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishEndpointBoundAsync(
        GameSessionEndpointSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var context = new GameEndpointBindingContext(
            snapshot.Endpoint,
            snapshot.ConnectionId,
            snapshot.CallbackContractTypes);
        foreach (var handler in _lifecycleHandlers)
        {
            try
            {
                await handler.OnEndpointBoundAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Game session endpoint-bound lifecycle handler failed for {ConnectionId}.",
                    snapshot.ConnectionId);
            }
        }
    }

    private async ValueTask PublishSessionTerminatedAsync(
        SessionTerminationNotice notice,
        CancellationToken cancellationToken)
    {
        var context = new GameSessionTerminationContext(notice);
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
                    notice.Session.OwnerKey);
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
