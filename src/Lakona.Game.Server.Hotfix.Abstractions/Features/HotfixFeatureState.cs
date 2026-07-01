namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixFeatureState
{
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
