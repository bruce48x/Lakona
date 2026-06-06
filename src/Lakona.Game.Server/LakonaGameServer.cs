using Lakona.Game.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server;

public sealed class LakonaGameServer : ILakonaGameServer
{
    private readonly IGameSessionDirectory _sessions;
    private readonly IGameSessionResumeService _resume;
    private readonly IReliablePushOutbox _reliablePush;
    private readonly IReliablePushAckService _reliablePushAcks;
    private readonly IGameSessionEndpointCloser _endpointCloser;

    public LakonaGameServer(
        IGameSessionDirectory sessions,
        IGameSessionResumeService resume,
        IReliablePushOutbox reliablePush,
        IReliablePushAckService reliablePushAcks,
        IGameSessionEndpointCloser endpointCloser)
    {
        _sessions = sessions;
        _resume = resume;
        _reliablePush = reliablePush;
        _reliablePushAcks = reliablePushAcks;
        _endpointCloser = endpointCloser;
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

    public ValueTask BindEndpointAsync<TCallback>(
        GameSessionKey session,
        GameEndpointName endpointName,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class
    {
        return _sessions.BindEndpointAsync(
            new SessionEndpointKey(session, endpointName),
            connectionId,
            callback,
            cancellationToken);
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
