using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Identifies one named endpoint within a game session.
/// </summary>
/// <param name="Session">The game session that owns the endpoint.</param>
/// <param name="EndpointName">The endpoint name within the session.</param>
public readonly record struct SessionEndpointKey(
    GameSessionKey Session,
    GameEndpointName EndpointName);
