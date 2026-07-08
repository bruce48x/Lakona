namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorHostCandidate(
    string NodeId,
    IReadOnlyDictionary<string, string>? Metadata = null);
