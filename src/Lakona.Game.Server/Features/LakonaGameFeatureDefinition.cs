namespace Lakona.Game.Server.Features;

/// <summary>
/// Describes one stable game-server feature registered with the host.
/// </summary>
public sealed class LakonaGameFeatureDefinition
{
    private readonly List<string> _after = [];
    private readonly List<string> _requiredFeatures = [];
    private readonly List<string> _requiredTransports = [];

    internal LakonaGameFeatureDefinition(string name, Type implementationType)
    {
        Name = name;
        ImplementationType = implementationType;
    }

    /// <summary>
    /// Gets the feature name used by configuration and cluster discovery.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the concrete <see cref="LakonaGameFeature"/> implementation type.
    /// </summary>
    public Type ImplementationType { get; }

    /// <summary>
    /// Gets feature names that should start before this feature when they are active.
    /// </summary>
    public IReadOnlyList<string> AfterFeatures => _after;

    /// <summary>
    /// Gets feature names that must also be active when this feature is active.
    /// </summary>
    public IReadOnlyList<string> RequiredFeatures => _requiredFeatures;

    /// <summary>
    /// Gets transport names required by this feature.
    /// </summary>
    public IReadOnlyList<string> RequiredTransports => _requiredTransports;

    /// <summary>
    /// Gets a value indicating whether this feature requires cluster configuration.
    /// </summary>
    public bool IsClusterRequired { get; private set; }

    /// <summary>
    /// Declares that this feature should start after another active feature.
    /// </summary>
    /// <param name="featureName">The feature name that should start first.</param>
    /// <returns>The same definition for chaining.</returns>
    public LakonaGameFeatureDefinition After(string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        _after.Add(featureName);
        return this;
    }

    /// <summary>
    /// Declares that another feature must be active when this feature is active.
    /// </summary>
    /// <param name="featureName">The required feature name.</param>
    /// <returns>The same definition for chaining.</returns>
    public LakonaGameFeatureDefinition RequiresFeature(string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        _requiredFeatures.Add(featureName);
        return this;
    }

    /// <summary>
    /// Declares that the host must configure a client-facing endpoint for a transport.
    /// </summary>
    /// <param name="transport">The required transport name.</param>
    /// <returns>The same definition for chaining.</returns>
    public LakonaGameFeatureDefinition RequiresTransport(string transport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        _requiredTransports.Add(transport);
        return this;
    }

    /// <summary>
    /// Declares that the host must enable cluster configuration for this feature.
    /// </summary>
    /// <returns>The same definition for chaining.</returns>
    public LakonaGameFeatureDefinition RequiresCluster()
    {
        IsClusterRequired = true;
        return this;
    }
}
