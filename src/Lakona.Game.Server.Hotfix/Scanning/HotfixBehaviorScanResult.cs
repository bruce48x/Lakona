using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix.Scanning;

public sealed record HotfixBehaviorScanResult(
    IReadOnlyList<HotfixMethodBinding> Methods,
    IReadOnlyList<HotfixServiceMethodBinding> Services,
    IReadOnlyList<string> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}
