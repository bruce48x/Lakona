namespace Lakona.Game.Server.Features;

public sealed class LakonaGameFeatureCatalog
{
    public LakonaGameFeatureCatalog(
        IReadOnlyList<LakonaGameFeatureDefinition> activeDefinitions,
        IReadOnlyList<LakonaGameFeature>? activeFeatures = null)
    {
        ActiveDefinitions = activeDefinitions;
        ActiveNames = activeDefinitions.Select(definition => definition.Name).ToArray();
        ActiveFeatures = activeFeatures ?? Array.Empty<LakonaGameFeature>();
    }

    public IReadOnlyList<LakonaGameFeatureDefinition> ActiveDefinitions { get; }

    public IReadOnlyList<string> ActiveNames { get; }

    public IReadOnlyList<LakonaGameFeature> ActiveFeatures { get; }
}
