using System.Reflection;

namespace Lakona.ProjectSystem.Packaging;

internal static class PackageToolVersion
{
    public static string Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0-local";
    }
}
