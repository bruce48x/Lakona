using System.Xml.Linq;

namespace Lakona.RepositoryGuards.Tests.ProjectSystemConsumers;

internal sealed class ProjectSystemReleaseInputs
{
    private const string ProjectPath = "src/Lakona.ProjectSystem/Lakona.ProjectSystem.csproj";

    private readonly HashSet<string> paths;

    private ProjectSystemReleaseInputs(IEnumerable<string> paths)
    {
        this.paths = paths
            .Select(Normalize)
            .ToHashSet(StringComparer.Ordinal);
    }

    public static ProjectSystemReleaseInputs Create(params string[] paths) => new(paths);

    public static ProjectSystemReleaseInputs ReadCurrent(string repositoryRoot)
    {
        var projectPath = Path.Combine(repositoryRoot, ProjectPath.Replace('/', Path.DirectorySeparatorChar));
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var paths = XDocument.Load(projectPath)
            .Descendants("XmlPeek")
            .Where(element => string.Equals(
                element.Attribute("Query")?.Value,
                "/Project/PropertyGroup/Version/text()",
                StringComparison.Ordinal))
            .Select(element => element.Attribute("XmlInputPath")?.Value)
            .OfType<string>()
            .Select(path => Resolve(repositoryRoot, projectDirectory, path));
        return new ProjectSystemReleaseInputs(paths);
    }

    public bool Contains(string path)
    {
        var normalized = Normalize(path);
        return IsProjectSystemBuildInput(normalized) ||
               IsPublicSkillPackInput(normalized) ||
               IsRepositoryBuildInput(normalized) ||
               paths.Contains(normalized);
    }

    private static bool IsProjectSystemBuildInput(string path) =>
        path.StartsWith("src/Lakona.ProjectSystem/", StringComparison.Ordinal) &&
        !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static bool IsPublicSkillPackInput(string path) =>
        path.StartsWith("skills/", StringComparison.Ordinal);

    private static bool IsRepositoryBuildInput(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName is "Directory.Build.props" or "Directory.Build.targets" or "global.json" ||
               path.Contains("/build/", StringComparison.OrdinalIgnoreCase) &&
               (path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase));
    }

    private static string Resolve(string repositoryRoot, string projectDirectory, string path)
    {
        var expanded = path
            .Replace("$(MSBuildProjectDirectory)", projectDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.GetFullPath(Path.Combine(projectDirectory, expanded));
        return Path.GetRelativePath(repositoryRoot, absolutePath);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
