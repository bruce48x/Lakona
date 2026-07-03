using Lakona.Game.Server.Hotfix.Dispatch;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix.Scanning;

public sealed record HotfixBehaviorScanResult(
    IReadOnlyList<HotfixMethodBinding> Methods,
    IReadOnlyList<HotfixServiceMethodBinding> Services,
    IReadOnlyList<HotfixFeatureDeclaration> Features,
    IReadOnlyList<HotfixActorMethodDescriptor> ActorMethods,
    IReadOnlyList<string> Diagnostics)
{
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
