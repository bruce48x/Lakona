using Lakona.Game.Server.Hotfix.Dispatch;

namespace Lakona.Game.Server.Hotfix.Scanning;

public sealed record HotfixSystemScanResult(
    IReadOnlyList<HotfixMethodBinding> Methods,
    IReadOnlyList<string> Diagnostics)
{
    public bool Succeeded => Diagnostics.Count == 0;
}
