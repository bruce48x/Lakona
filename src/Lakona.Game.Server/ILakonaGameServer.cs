using Lakona.Game.Abstractions;
using Lakona.Game.Server.ReliablePush;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server;

public interface ILakonaGameServer
{
    ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
        string ownerKey,
        GameEndpointName endpointName,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
        GameSessionResumeRequest request,
        GameEndpointName endpointName,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask BindEndpointAsync<TCallback>(
        GameSessionKey session,
        GameEndpointName endpointName,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask MarkEndpointDisconnectedAsync(
        GameSessionKey session,
        GameEndpointName endpointName,
        string? connectionId = null,
        CancellationToken cancellationToken = default);

    ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        GameEndpointName endpointName,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask TerminateSessionAsync(
        GameSessionKey session,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask TerminateSessionAsync(
        GameSessionKey session,
        GameEndpointName endpointName,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default);

    ValueTask<long> PublishReliablePushAsync<TCallback, TPayload>(
        GameSessionKey session,
        GameEndpointName endpointName,
        string kind,
        TPayload payload,
        ReliablePushDeliver<TCallback, TPayload> deliver,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<long> PublishReliablePushAsync(
        GameSessionKey session,
        string kind,
        object payload,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default);

    ValueTask ReplayReliablePushAsync(
        GameSessionKey session,
        Func<ReliablePushRecord, ValueTask> deliver,
        CancellationToken cancellationToken = default);

    ValueTask ReplayReliablePushAsync<TCallback, TPayload>(
        GameSessionKey session,
        GameEndpointName endpointName,
        string kind,
        ReliablePushDeliver<TCallback, TPayload> deliver,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<ReliablePushAckOutcome> AckReliablePushAsync(
        GameSessionKey currentSession,
        GameSessionKey acknowledgedSession,
        long sequence,
        CancellationToken cancellationToken = default);
}
