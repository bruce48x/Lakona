namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record ActorPlacementContext<TKey>(
    IReadOnlyList<ActorHostCandidate> Candidates,
    TKey Key);
