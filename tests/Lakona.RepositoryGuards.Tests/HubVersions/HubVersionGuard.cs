using Lakona.RepositoryGuards.Tests.PackageVersions;
using Lakona.RepositoryGuards.Tests.ProjectSystemConsumers;

namespace Lakona.RepositoryGuards.Tests.HubVersions;

internal static class HubVersionGuard
{
    private static readonly string[] ReleaseInputPrefixes =
    [
        "src/Lakona.Hub/",
        "scripts/hub/"
    ];

    private static readonly HashSet<string> ReleaseInputFiles = new(StringComparer.Ordinal)
    {
        ".github/workflows/publish-hub.yml",
        ".github/workflows/tests-linux.yml"
    };

    internal static VersionGuardScope CreateScope(ProjectSystemReleaseInputs projectSystemInputs) => new(
        HubVersionProjectReader.ProjectPath,
        "LAKONA_HUB_VERSION_GUARD_BASE",
        "LAKONA_HUB_VERSION_GUARD_HEAD",
        "Hub version guard",
        path => IsReleaseInputPath(path) || projectSystemInputs.Contains(path));

    public static HubVersionGuardResult Evaluate(
        string repositoryRoot,
        string baseVersion,
        string headVersion,
        IReadOnlyCollection<string> changedPaths)
    {
        var changedInputs = changedPaths
            .Select(path => ToRepositoryRelativePath(repositoryRoot, path))
            .Where(IsReleaseInputPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var versionChanged = !string.Equals(baseVersion, headVersion, StringComparison.Ordinal);
        return new HubVersionGuardResult(baseVersion, headVersion, versionChanged, changedInputs);
    }

    internal static bool IsReleaseInputPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return ReleaseInputFiles.Contains(normalized) ||
               ReleaseInputPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string ToRepositoryRelativePath(string repositoryRoot, string path)
    {
        var normalized = path.Replace('\\', '/');
        var root = Path.GetFullPath(repositoryRoot).Replace('\\', '/').TrimEnd('/') + "/";
        return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? normalized[root.Length..]
            : normalized.TrimStart('/');
    }
}

internal sealed record HubVersionGuardResult(
    string BaseVersion,
    string HeadVersion,
    bool VersionChanged,
    IReadOnlyList<string> ChangedInputs)
{
    public bool Succeeded => ChangedInputs.Count == 0 || VersionChanged;
}
