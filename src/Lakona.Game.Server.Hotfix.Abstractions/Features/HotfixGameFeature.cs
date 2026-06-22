namespace Lakona.Game.Server.Hotfix.Abstractions;

public abstract class HotfixGameFeature
{
    public virtual bool Discoverable => true;

    public virtual IReadOnlyDictionary<string, string> Metadata { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public abstract void Configure(HotfixFeatureContext context);
}
