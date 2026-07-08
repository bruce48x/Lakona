using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Scanning;

public sealed record HotfixBehaviorScanResult(
    IReadOnlyList<HotfixMethodBinding> Methods,
    IReadOnlyList<HotfixServiceMethodBinding> Services,
    IReadOnlyList<HotfixFeatureDeclaration> Features,
    IReadOnlyList<HotfixActorMethodDescriptor> ActorMethods,
    IReadOnlyList<ActorStartupDeclaration> ActorStartups,
    IReadOnlyList<ActorPlacementDeclaration> ActorPlacements,
    IReadOnlyList<HotfixActorLifecycleDescriptor> ActorLifecycles,
    IReadOnlyList<ServiceDescriptor> StartupServices,
    IReadOnlyList<string> Diagnostics)
{
    public HotfixBehaviorScanResult(
        IReadOnlyList<HotfixMethodBinding> methods,
        IReadOnlyList<HotfixServiceMethodBinding> services,
        IReadOnlyList<HotfixFeatureDeclaration> features,
        IReadOnlyList<HotfixActorMethodDescriptor> actorMethods,
        IReadOnlyList<string> diagnostics)
        : this(
            methods,
            services,
            features,
            actorMethods,
            Array.Empty<ActorStartupDeclaration>(),
            Array.Empty<ActorPlacementDeclaration>(),
            Array.Empty<HotfixActorLifecycleDescriptor>(),
            Array.Empty<ServiceDescriptor>(),
            diagnostics)
    {
    }

    public HotfixBehaviorScanResult(
        IReadOnlyList<HotfixMethodBinding> methods,
        IReadOnlyList<HotfixServiceMethodBinding> services,
        IReadOnlyList<HotfixFeatureDeclaration> features,
        IReadOnlyList<string> diagnostics)
        : this(methods, services, features, Array.Empty<HotfixActorMethodDescriptor>(), diagnostics)
    {
    }

    public bool Succeeded => Diagnostics.Count == 0;
}
