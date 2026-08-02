namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal static class PackageVersionGuard
{
    public static PackageVersionGuardResult Evaluate(
        IReadOnlyList<PackageProject> baseProjects,
        IReadOnlyList<PackageProject> headProjects,
        IReadOnlyCollection<string> changedPaths)
    {
        var baseByPath = baseProjects.ToDictionary(project => project.ProjectPath, StringComparer.Ordinal);
        var graph = PackageGraph.Create(headProjects);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        var work = new Queue<(string PropagationSource, string RootCause)>();
        var repositoryRoot = FindRepositoryRoot(headProjects);

        foreach (var project in headProjects)
        {
            var artifactChanged = HasArtifactChange(project, baseByPath, changedPaths, repositoryRoot);
            if (!artifactChanged)
                continue;

            required.Add(project.ProjectPath);
            reasons[project.ProjectPath] = $"{project.PackageId} changed its packed inputs.";
            if (HasDirectContentChange(project, changedPaths, repositoryRoot))
                work.Enqueue((project.ProjectPath, project.ProjectPath));
        }

        while (work.Count > 0)
        {
            var (propagationSource, rootCause) = work.Dequeue();
            foreach (var consumerPath in graph.ConsumersOf(propagationSource))
            {
                if (required.Add(consumerPath))
                {
                    var chain = graph.DescribePath(consumerPath, rootCause);
                    reasons[consumerPath] = $"{chain} requires a new consumer package version because {graph[rootCause].PackageId} changed.";
                }

                work.Enqueue((consumerPath, rootCause));
            }
        }

        var failures = required
            .Select(path => graph[path])
            .Where(project => !VersionChanged(project, baseByPath))
            .Select(project => new PackageVersionFailure(project.PackageId, project.Version, reasons[project.ProjectPath]))
            .OrderBy(failure => failure.PackageId, StringComparer.Ordinal)
            .ToArray();

        return new PackageVersionGuardResult(failures);
    }

    private static bool HasDirectContentChange(
        PackageProject project,
        IReadOnlyCollection<string> changedPaths,
        string repositoryRoot)
    {
        if (changedPaths.Any(path => IsRepositoryLevelBuildInput(path, repositoryRoot)))
            return true;

        if (HasPackedInputChange(project, changedPaths))
            return true;

        var directory = Path.GetDirectoryName(project.ProjectPath)!.Replace('\\', '/').TrimEnd('/') + "/";
        return changedPaths.Any(path => PackageProjectReader.NormalizePath(path).StartsWith(directory, StringComparison.Ordinal));
    }

    private static bool HasArtifactChange(
        PackageProject project,
        IReadOnlyDictionary<string, PackageProject> baseByPath,
        IReadOnlyCollection<string> changedPaths,
        string repositoryRoot)
    {
        if (VersionChanged(project, baseByPath))
            return true;

        return HasDirectContentChange(project, changedPaths, repositoryRoot);
    }

    private static bool HasPackedInputChange(PackageProject project, IReadOnlyCollection<string> changedPaths)
    {
        return project.PackedInputPaths.Any(input =>
        {
            var projectInputDirectory = input.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(input)!.Replace('\\', '/').TrimEnd('/') + "/"
                : null;
            return changedPaths.Any(path =>
            {
                var normalizedPath = PackageProjectReader.NormalizePath(path);
                return string.Equals(normalizedPath, input, StringComparison.Ordinal) ||
                       projectInputDirectory is not null &&
                       normalizedPath.StartsWith(projectInputDirectory, StringComparison.Ordinal);
            });
        });
    }

    private static bool IsRepositoryLevelBuildInput(string path, string repositoryRoot)
    {
        var normalized = PackageProjectReader.NormalizePath(path);
        if (string.Equals(normalized, $"{repositoryRoot}/Directory.Build.props", StringComparison.Ordinal) ||
            string.Equals(normalized, $"{repositoryRoot}/Directory.Build.targets", StringComparison.Ordinal) ||
            string.Equals(normalized, $"{repositoryRoot}/global.json", StringComparison.Ordinal))
            return true;

        return normalized.StartsWith($"{repositoryRoot}/build/", StringComparison.OrdinalIgnoreCase) &&
               (normalized.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith(".targets", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot(IReadOnlyList<PackageProject> projects)
    {
        var projectPath = projects.FirstOrDefault()?.ProjectPath ??
                          throw new InvalidOperationException("Cannot resolve the repository root without package projects.");
        var markerIndex = projectPath.LastIndexOf("/src/", StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0
            ? projectPath[..markerIndex]
            : throw new InvalidOperationException($"Package project is not under the repository src directory: {projectPath}");
    }

    private static bool VersionChanged(PackageProject project, IReadOnlyDictionary<string, PackageProject> baseByPath)
    {
        return !baseByPath.TryGetValue(project.ProjectPath, out var baseProject) ||
               !string.Equals(baseProject.Version, project.Version, StringComparison.Ordinal);
    }
}
