using System.Reflection;

namespace Lakona.Game.Server.Features;

public static class LakonaGameFeatureDiscovery
{
    public static IReadOnlyList<LakonaGameFeatureDefinition> Discover(
        Assembly assembly,
        IReadOnlyList<Type>? featureTypes = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var candidates = featureTypes ?? GetLoadableTypes(assembly)
            .Where(IsConcreteFeature)
            .ToArray();

        return ToDefinitions(candidates);
    }

    public static IReadOnlyList<LakonaGameFeatureDefinition> DiscoverLoadedAssemblies()
    {
        var candidates = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .SelectMany(GetLoadableTypes)
            .Where(IsConcreteFeature)
            .ToArray();

        return ToDefinitions(candidates);
    }

    private static IReadOnlyList<LakonaGameFeatureDefinition> ToDefinitions(IEnumerable<Type> featureTypes)
    {
        var definitions = new List<LakonaGameFeatureDefinition>();
        var names = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        foreach (var featureType in featureTypes)
        {
            if (!IsConcreteFeature(featureType))
            {
                continue;
            }

            var name = LakonaGameFeatureName.FromType(featureType);
            if (names.TryGetValue(name, out var existing))
            {
                throw new InvalidOperationException(
                    $"Lakona.Game feature name '{name}' is used by both {existing.FullName} and {featureType.FullName}.");
            }

            names.Add(name, featureType);
            definitions.Add(new LakonaGameFeatureDefinition(name, featureType));
        }

        return definitions
            .OrderBy(definition => definition.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsConcreteFeature(Type type)
    {
        return typeof(LakonaGameFeature).IsAssignableFrom(type)
            && !type.IsAbstract
            && !type.IsInterface;
    }

    private static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }
}
