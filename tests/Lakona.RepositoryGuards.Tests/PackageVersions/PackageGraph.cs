namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal sealed class PackageGraph
{
    private readonly Dictionary<string, PackageProject> byPath;
    private readonly Dictionary<string, List<string>> reverseEdges = new(StringComparer.Ordinal);

    private PackageGraph(Dictionary<string, PackageProject> byPath)
    {
        this.byPath = byPath;
    }

    public static PackageGraph Create(IReadOnlyList<PackageProject> projects)
    {
        var byPath = projects.ToDictionary(project => project.ProjectPath, StringComparer.Ordinal);
        var graph = new PackageGraph(byPath);
        foreach (var project in projects)
        {
            foreach (var dependencyPath in project.ProjectReferences.Concat(project.VersionSourceReferences))
            {
                if (!byPath.ContainsKey(dependencyPath))
                    continue;

                if (!graph.reverseEdges.TryGetValue(dependencyPath, out var consumers))
                {
                    consumers = [];
                    graph.reverseEdges.Add(dependencyPath, consumers);
                }

                consumers.Add(project.ProjectPath);
            }
        }

        return graph;
    }

    public PackageProject this[string projectPath] => byPath[projectPath];

    public IEnumerable<string> ConsumersOf(string dependencyPath)
    {
        return reverseEdges.TryGetValue(dependencyPath, out var consumers) ? consumers : [];
    }

    public string DescribePath(string consumerPath, string dependencyPath)
    {
        var queue = new Queue<(string Path, List<string> Chain)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue((consumerPath, [consumerPath]));
        visited.Add(consumerPath);

        while (queue.Count > 0)
        {
            var (current, chain) = queue.Dequeue();
            if (string.Equals(current, dependencyPath, StringComparison.Ordinal))
                return string.Join(" -> ", chain.Select(path => byPath[path].PackageId));

            foreach (var next in byPath[current].ProjectReferences.Concat(byPath[current].VersionSourceReferences))
            {
                if (!byPath.ContainsKey(next) || !visited.Add(next))
                    continue;
                queue.Enqueue((next, [.. chain, next]));
            }
        }

        return $"{byPath[consumerPath].PackageId} -> {byPath[dependencyPath].PackageId}";
    }
}
