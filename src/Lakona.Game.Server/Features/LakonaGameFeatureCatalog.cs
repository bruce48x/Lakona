namespace Lakona.Game.Server.Features;

/// <summary>
/// Contains the stable features selected for the current server process.
/// </summary>
public sealed class LakonaGameFeatureCatalog
{
    /// <summary>
    /// Initializes a new feature catalog.
    /// </summary>
    /// <param name="activeDefinitions">The active feature definitions in startup order.</param>
    /// <param name="activeFeatures">The instantiated active features, when available.</param>
    public LakonaGameFeatureCatalog(
        IReadOnlyList<LakonaGameFeatureDefinition> activeDefinitions,
        IReadOnlyList<LakonaGameFeature>? activeFeatures = null)
    {
        ActiveDefinitions = activeDefinitions;
        ActiveNames = activeDefinitions.Select(definition => definition.Name).ToArray();
        ActiveFeatures = activeFeatures ?? Array.Empty<LakonaGameFeature>();
    }

    /// <summary>
    /// Gets active feature definitions in startup order.
    /// </summary>
    public IReadOnlyList<LakonaGameFeatureDefinition> ActiveDefinitions { get; }

    /// <summary>
    /// Gets active feature names in startup order.
    /// </summary>
    public IReadOnlyList<string> ActiveNames { get; }

    /// <summary>
    /// Gets instantiated active feature objects in startup order.
    /// </summary>
    public IReadOnlyList<LakonaGameFeature> ActiveFeatures { get; }
}
