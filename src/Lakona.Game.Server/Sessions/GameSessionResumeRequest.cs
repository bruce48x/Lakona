using Lakona.Game.Abstractions;

namespace Lakona.Game.Server.Sessions;

public sealed record GameSessionResumeRequest(
    GameSessionKey Session,
    string? Token = null);
