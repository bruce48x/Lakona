using System.Reflection;

internal static class ToolVersion
{
    public static string Current { get; } = ResolveCurrent();

    private static string ResolveCurrent()
    {
        var version = typeof(ToolVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        version ??= typeof(ToolVersion).Assembly.GetName().Version?.ToString();
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        var metadataIndex = version.IndexOf('+', StringComparison.Ordinal);
        return metadataIndex >= 0 ? version[..metadataIndex] : version;
    }
}
