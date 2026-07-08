namespace Lakona.Game.Server.Actors;

public sealed record ActorHostDescriptor(
    string Actor,
    string PolicyHash,
    string BuildTag,
    IReadOnlyDictionary<string, string>? Metadata = null);
