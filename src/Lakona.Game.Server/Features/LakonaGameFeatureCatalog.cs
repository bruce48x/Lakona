namespace Lakona.Game.Server.Features;

public sealed class LakonaGameFeatureCatalog
{
    public LakonaGameFeatureCatalog(IReadOnlyList<LakonaGameFeatureDefinition> activeDefinitions)
    {
        ActiveDefinitions = activeDefinitions;
        ActiveNames = activeDefinitions.Select(definition => definition.Name).ToArray();
    }

    public IReadOnlyList<LakonaGameFeatureDefinition> ActiveDefinitions { get; }

    public IReadOnlyList<string> ActiveNames { get; }
}
