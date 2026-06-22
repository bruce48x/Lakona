namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixFeatureDeclaration(
    string Name,
    Type FeatureType,
    bool Discoverable,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<HotfixActorTickDeclaration> ActorTicks);
