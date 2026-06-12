using System.Reflection;

namespace Lakona.Game.Server.Hotfix.BuildTag;

public static class HotfixBuildTag
{
    public const string MetadataName = "LakonaHotfixBuildTag";

    public static string Get(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == MetadataName)
            ?.Value
            ?? string.Empty;
    }
}
