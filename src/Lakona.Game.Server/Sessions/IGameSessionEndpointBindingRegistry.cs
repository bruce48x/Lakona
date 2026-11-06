using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

/// <summary>Supports creating a session before its first endpoint connection is known.</summary>
public interface IGameSessionEndpointBindingRegistry : IGameSessionRegistry
{
    /// <summary>Reserves an endpoint binding and reports whether it will be the session's first committed binding.</summary>
    /// <remarks>
    /// Determine <see cref="GameSessionBindResult.IsFirstBinding"/> as part of reserving the binding.
    /// Stage the endpoint's reliable-push policy and serializer profile until commit. Rolling back a first
    /// reservation must allow a subsequent attempt on an endpoint with a different policy or profile.
    /// After commit, enforce the session's established policy on every replacement, including after disconnect.
    /// </remarks>
    ValueTask<GameSessionBindResult> PrepareEndpointSessionBindingAsync(
        GameSessionKey session,
        string connectionId,
        bool reliablePush,
        string? serializerProfile,
        CancellationToken cancellationToken = default);
}
