using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

/// <summary>
/// Describes a client request to resume a game session.
/// </summary>
/// <param name="Session">The game session the client is trying to resume.</param>
/// <param name="Token">An optional resume token supplied by the client.</param>
public sealed record GameSessionResumeRequest(
    GameSessionKey Session,
    string? Token = null);
