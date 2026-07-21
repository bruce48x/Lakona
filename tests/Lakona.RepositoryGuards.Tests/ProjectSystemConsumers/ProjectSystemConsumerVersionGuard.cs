namespace Lakona.RepositoryGuards.Tests.ProjectSystemConsumers;

internal static class ProjectSystemConsumerVersionGuard
{
    public static ProjectSystemConsumerVersionGuardResult Evaluate(
        string consumerName,
        string repositoryRoot,
        string baseVersion,
        string headVersion,
        IReadOnlyCollection<string> changedPaths,
        ProjectSystemReleaseInputs inputs)
    {
        var changedInputs = changedPaths
            .Select(path => ToRepositoryRelativePath(repositoryRoot, path))
            .Where(inputs.Contains)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var versionChanged = !string.Equals(baseVersion, headVersion, StringComparison.Ordinal);
        return new ProjectSystemConsumerVersionGuardResult(
            consumerName,
            baseVersion,
            headVersion,
            versionChanged,
            changedInputs);
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

internal sealed record ProjectSystemConsumerVersionGuardResult(
    string ConsumerName,
    string BaseVersion,
    string HeadVersion,
    bool VersionChanged,
    IReadOnlyList<string> ChangedInputs)
{
    public bool Succeeded => ChangedInputs.Count == 0 || VersionChanged;
}
