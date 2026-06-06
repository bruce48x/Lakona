using Lakona.Game.Server.Configuration;

namespace Lakona.Game.Server.Features;

public sealed class LakonaGameFeatureCatalogBuilder
{
    private readonly List<LakonaGameFeatureDefinition> _definitions = [];
    private readonly Dictionary<string, LakonaGameFeatureDefinition> _byName = new(StringComparer.OrdinalIgnoreCase);

    public LakonaGameFeatureDefinition Feature<TFeature>(string name)
        where TFeature : LakonaGameFeature
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_byName.ContainsKey(name))
        {
            throw new InvalidOperationException($"Lakona.Game feature '{name}' is already registered.");
        }

        var definition = new LakonaGameFeatureDefinition(name, typeof(TFeature));
        _definitions.Add(definition);
        _byName.Add(name, definition);
        return definition;
    }

    internal LakonaGameFeatureCatalog Build(LakonaGameRuntimeOptions options)
    {
        var active = ResolveActiveDefinitions(options.Feature);
        ValidateRequiredFeatures(active);
        return new LakonaGameFeatureCatalog(SortAfterDependencies(active));
    }

    private IReadOnlyList<LakonaGameFeatureDefinition> ResolveActiveDefinitions(IReadOnlyList<string>? configuredFeatures)
    {
        if (configuredFeatures is null)
        {
            return _definitions.ToArray();
        }

        var active = new List<LakonaGameFeatureDefinition>();
        foreach (var featureName in configuredFeatures)
        {
            if (!_byName.TryGetValue(featureName, out var definition))
            {
                var available = string.Join(", ", _definitions.Select(candidate => candidate.Name));
                throw new InvalidOperationException(
                    $"Lakona.Game feature '{featureName}' was configured but is not registered. Available features: {available}.");
            }

            active.Add(definition);
        }

        return active;
    }

    private static void ValidateRequiredFeatures(IReadOnlyList<LakonaGameFeatureDefinition> active)
    {
        var activeNames = active
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in active)
        {
            foreach (var requiredFeature in definition.RequiredFeatures)
            {
                if (!activeNames.Contains(requiredFeature))
                {
                    throw new InvalidOperationException(
                        $"Lakona.Game feature '{definition.Name}' requires feature '{requiredFeature}', but '{requiredFeature}' is not active.");
                }
            }
        }
    }

    private static IReadOnlyList<LakonaGameFeatureDefinition> SortAfterDependencies(
        IReadOnlyList<LakonaGameFeatureDefinition> active)
    {
        var remaining = active.ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<LakonaGameFeatureDefinition>(active.Count);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in active)
        {
            Visit(definition, remaining, ordered, visiting, visited);
        }

        return ordered;
    }

    private static void Visit(
        LakonaGameFeatureDefinition definition,
        IReadOnlyDictionary<string, LakonaGameFeatureDefinition> active,
        List<LakonaGameFeatureDefinition> ordered,
        HashSet<string> visiting,
        HashSet<string> visited)
    {
        if (visited.Contains(definition.Name))
        {
            return;
        }

        if (!visiting.Add(definition.Name))
        {
            throw new InvalidOperationException($"Lakona.Game feature dependency cycle includes '{definition.Name}'.");
        }

        foreach (var dependencyName in definition.AfterFeatures)
        {
            if (active.TryGetValue(dependencyName, out var dependency))
            {
                Visit(dependency, active, ordered, visiting, visited);
            }
        }

        visiting.Remove(definition.Name);
        visited.Add(definition.Name);
        ordered.Add(definition);
    }
}
