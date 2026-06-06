namespace Lakona.Game.Server.Hotfix.Loading;

internal static class PathValidation
{
    public static string RequireSafeFileName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar)
            || value is "." or ".."
            || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Value must be a simple file name without roots, traversal, or directory separators.", parameterName);
        }

        return value;
    }

    public static string GetContainedPath(string baseDirectory, string relativeName, string parameterName)
    {
        var fullBaseDirectory = Path.GetFullPath(baseDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(fullBaseDirectory, relativeName));
        var baseWithSeparator = Path.EndsInDirectorySeparator(fullBaseDirectory)
            ? fullBaseDirectory
            : fullBaseDirectory + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!fullPath.StartsWith(baseWithSeparator, comparison))
        {
            throw new ArgumentException("Resolved path must remain under the expected base directory.", parameterName);
        }

        return fullPath;
    }
}
