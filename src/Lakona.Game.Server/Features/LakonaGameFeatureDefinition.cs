namespace Lakona.Game.Server.Features;

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

    public string Name { get; }

    public Type ImplementationType { get; }

    public IReadOnlyList<string> AfterFeatures => _after;

    public IReadOnlyList<string> RequiredFeatures => _requiredFeatures;

    public IReadOnlyList<string> RequiredTransports => _requiredTransports;

    public bool IsClusterRequired { get; private set; }

    public LakonaGameFeatureDefinition After(string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        _after.Add(featureName);
        return this;
    }

    public LakonaGameFeatureDefinition RequiresFeature(string featureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        _requiredFeatures.Add(featureName);
        return this;
    }

    public LakonaGameFeatureDefinition RequiresTransport(string transport)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transport);
        _requiredTransports.Add(transport);
        return this;
    }

    public LakonaGameFeatureDefinition RequiresCluster()
    {
        IsClusterRequired = true;
        return this;
    }
}
