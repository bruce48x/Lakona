using Lakona.Game.Abstractions;
using Lakona.Game.Server.Sessions;

namespace Lakona.Game.Server;

public interface ILakonaGameServer
{
    ValueTask<GameSessionKey> StartSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionKey> StartSessionAsync<TCallback>(
        string ownerKey,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<SessionResumeDecision> ResumeSessionAsync<TCallback>(
        GameSessionResumeRequest request,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask BindSessionAsync<TCallback>(
        GameSessionKey session,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask BindCurrentSessionAsync<TCallback>(
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask MarkSessionDisconnectedAsync(
        GameSessionKey session,
        string? connectionId = null,
        CancellationToken cancellationToken = default);

    ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        GameSessionKey session,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask TerminateSessionAsync(
        GameSessionKey session,
        SessionTerminationReason reason,
        string? message = null,
        SessionTerminationOptions? options = null,
        CancellationToken cancellationToken = default);
}
