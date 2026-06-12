using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public interface IGameSessionDirectory
{
    ValueTask<GameSessionKey> StartNewSessionAsync(
        string ownerKey,
        CancellationToken cancellationToken = default);

    ValueTask<SessionResumeDecision> TryResumeAsync(
        GameSessionKey session,
        CancellationToken cancellationToken = default);

    ValueTask<GameSessionEndpointBindResult> BindEndpointAsync<TCallback>(
        SessionEndpointKey endpoint,
        string connectionId,
        TCallback callback,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<IReadOnlyList<GameSessionEndpointSnapshot>> MarkConnectionDisconnectedAsync(
        string connectionId,
        CancellationToken cancellationToken = default);

    ValueTask MarkEndpointDisconnectedAsync(
        SessionEndpointKey endpoint,
        string? connectionId = null,
        CancellationToken cancellationToken = default);

    ValueTask MarkSessionTerminatedAsync(
        GameSessionKey session,
        SessionTerminationNotice notice,
        bool keepForResume,
        CancellationToken cancellationToken = default);

    ValueTask<TCallback?> GetCallbackAsync<TCallback>(
        SessionEndpointKey endpoint,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<GameSessionEndpointBinding<TCallback>?> GetEndpointBindingAsync<TCallback>(
        SessionEndpointKey endpoint,
        CancellationToken cancellationToken = default)
        where TCallback : class;

    ValueTask<IReadOnlyList<GameSessionEndpointSnapshot>> ExpireDisconnectedEndpointsAsync(
        DateTimeOffset disconnectedBefore,
        CancellationToken cancellationToken = default);
}
