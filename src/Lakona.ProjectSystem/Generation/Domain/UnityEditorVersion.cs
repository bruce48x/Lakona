using System.Text.RegularExpressions;

namespace Lakona.ProjectSystem.Generation.Domain;

internal sealed record UnityEditorInstallation(
    string ExecutablePath,
    string Version,
    string? Revision);

internal static partial class UnityEditorVersion
{
    public static bool IsCompatible(string actualVersion, string expectedVersion)
    {
        var actualStream = GetStream(actualVersion);
        var expectedStream = GetStream(expectedVersion);
        return actualStream is not null &&
               expectedStream is not null &&
               string.Equals(actualStream, expectedStream, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetStream(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var match = StreamPattern().Match(version.Trim());
        return match.Success ? $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}" : null;
    }

    [GeneratedRegex(@"^(?<major>\d+)\.(?<minor>\d+)\.")]
    private static partial Regex StreamPattern();
}
