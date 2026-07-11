namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record StartupActorSelectionContext<TKey>(
    IReadOnlyList<StartupActorCandidate> Candidates,
    TKey Key);
