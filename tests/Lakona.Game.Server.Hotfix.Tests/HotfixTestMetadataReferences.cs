using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Lakona.Game.Server.Hotfix.Tests;

internal static class HotfixTestMetadataReferences
{
    public static MetadataReference[] CreateDefaultReferences(params Type[] requiredTypes)
    {
        var assemblyLocations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .Select(static assembly => assembly.Location)
            .Where(static location => !string.IsNullOrWhiteSpace(location));

        return CreateDefaultReferences(assemblyLocations, requiredTypes);
    }

    public static MetadataReference[] CreateDefaultReferences(
        IEnumerable<string> assemblyLocations,
        IEnumerable<Type> requiredTypes)
    {
        var discoveredReferences = assemblyLocations
            .Select(TryCreateReference)
            .OfType<MetadataReference>();
        var requiredReferences = requiredTypes.Select(CreateRequiredReference);

        return discoveredReferences
            .Concat(requiredReferences)
            .Distinct(MetadataReferencePathComparer.Instance)
            .ToArray();
    }

    private static MetadataReference CreateRequiredReference(Type type)
    {
        var location = type.Assembly.Location;
        if (string.IsNullOrWhiteSpace(location) || !File.Exists(location))
        {
            throw new InvalidOperationException($"Required test reference assembly '{type.Assembly.FullName}' is not available on disk.");
        }

        return MetadataReference.CreateFromFile(location);
    }

    private static MetadataReference? TryCreateReference(string location)
    {
        if (!File.Exists(location))
        {
            return null;
        }

        try
        {
            return MetadataReference.CreateFromFile(location);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private sealed class MetadataReferencePathComparer : IEqualityComparer<MetadataReference>
    {
        public static readonly MetadataReferencePathComparer Instance = new();

        public bool Equals(MetadataReference? x, MetadataReference? y)
        {
            return string.Equals(x?.Display, y?.Display, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(MetadataReference obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Display ?? string.Empty);
        }
    }
}
