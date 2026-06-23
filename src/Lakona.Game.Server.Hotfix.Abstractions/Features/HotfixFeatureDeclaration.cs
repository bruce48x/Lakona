using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed record HotfixFeatureDeclaration(
    string Name,
    Type FeatureType,
    bool Discoverable,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<HotfixLocalActorDeclaration> LocalActors,
    IReadOnlyList<HotfixActorTickDeclaration> ActorTicks,
    IReadOnlyList<HotfixFeatureCommandDeclaration> Commands,
    IReadOnlyList<ServiceDescriptor> Services);
