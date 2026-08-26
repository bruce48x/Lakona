namespace Lakona.Game.Server.Actors;

public sealed record ActorHostDescriptor(
    string Actor,
    string PolicyHash,
    string HotfixVersion,
    IReadOnlyDictionary<string, string>? Metadata = null);
