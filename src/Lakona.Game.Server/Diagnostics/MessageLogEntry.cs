using Lakona.Game.Server.Actors;

namespace Lakona.Game.Server.Diagnostics;

public sealed record MessageLogEntry(
    DateTimeOffset Timestamp,
    object Message,
    string? Error);
