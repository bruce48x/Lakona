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

        foreach (var project in headProjects)
        {
            var artifactChanged = HasArtifactChange(project, baseByPath, changedPaths);
            if (!artifactChanged)
                continue;

            required.Add(project.ProjectPath);
            reasons[project.ProjectPath] = $"{project.PackageId} changed its packed inputs.";
            if (HasDirectContentChange(project, changedPaths))
                work.Enqueue((project.ProjectPath, project.ProjectPath));
        }

        RequireToolVersionBumpForPackageChanges(headProjects, baseByPath, required, reasons);

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

    private static void RequireToolVersionBumpForPackageChanges(
        IReadOnlyList<PackageProject> headProjects,
        IReadOnlyDictionary<string, PackageProject> baseByPath,
        HashSet<string> required,
        Dictionary<string, string> reasons)
    {
        var toolProject = headProjects.FirstOrDefault(project => string.Equals(project.PackageId, "Lakona.Tool", StringComparison.Ordinal));
        if (toolProject is null || VersionChanged(toolProject, baseByPath))
            return;

        var packageChanged = required.Any(path =>
            !string.Equals(path, toolProject.ProjectPath, StringComparison.Ordinal));
        if (!packageChanged)
            return;

        required.Add(toolProject.ProjectPath);
        reasons[toolProject.ProjectPath] = "Lakona.Tool version must change when any package in the release graph changes.";
    }

    private static bool HasDirectContentChange(PackageProject project, IReadOnlyCollection<string> changedPaths)
    {
        if (changedPaths.Any(IsRepositoryLevelBuildInput))
            return true;

        if (HasPackedInputChange(project, changedPaths))
            return true;

        var directory = Path.GetDirectoryName(project.ProjectPath)!.Replace('\\', '/').TrimEnd('/') + "/";
        return changedPaths.Any(path => PackageProjectReader.NormalizePath(path).StartsWith(directory, StringComparison.Ordinal));
    }

    private static bool HasArtifactChange(
        PackageProject project,
        IReadOnlyDictionary<string, PackageProject> baseByPath,
        IReadOnlyCollection<string> changedPaths)
    {
        if (VersionChanged(project, baseByPath))
            return true;

        return HasDirectContentChange(project, changedPaths);
    }

    private static bool HasPackedInputChange(PackageProject project, IReadOnlyCollection<string> changedPaths)
    {
        return project.PackedInputPaths.Any(input =>
            changedPaths.Any(path => string.Equals(PackageProjectReader.NormalizePath(path), input, StringComparison.Ordinal)));
    }

    private static bool IsRepositoryLevelBuildInput(string path)
    {
        var normalized = PackageProjectReader.NormalizePath(path);
        var fileName = Path.GetFileName(normalized);
        return fileName is "Directory.Build.props" or "Directory.Build.targets" or "global.json" ||
               normalized.Contains("/build/", StringComparison.OrdinalIgnoreCase) && (normalized.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".targets", StringComparison.OrdinalIgnoreCase));
    }

    private static bool VersionChanged(PackageProject project, IReadOnlyDictionary<string, PackageProject> baseByPath)
    {
        return !baseByPath.TryGetValue(project.ProjectPath, out var baseProject) ||
               !string.Equals(baseProject.Version, project.Version, StringComparison.Ordinal);
    }
}
