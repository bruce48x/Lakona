using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix.Scanning;

public sealed record HotfixBehaviorScanResult(
    IReadOnlyList<HotfixMethodBinding> Methods,
    IReadOnlyList<HotfixServiceMethodBinding> Services,
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
        IReadOnlyList<HotfixActorMethodDescriptor> actorMethods,
        IReadOnlyList<string> diagnostics)
        : this(
            methods,
            services,
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
        IReadOnlyList<string> diagnostics)
        : this(methods, services, Array.Empty<HotfixActorMethodDescriptor>(), diagnostics)
    {
    }

    public bool Succeeded => Diagnostics.Count == 0;
}
