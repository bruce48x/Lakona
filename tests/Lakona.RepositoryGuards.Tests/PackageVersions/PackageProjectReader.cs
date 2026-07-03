using System.Xml.Linq;

namespace Lakona.RepositoryGuards.Tests.PackageVersions;

internal static class PackageProjectReader
{
    public static IReadOnlyList<PackageProject> ReadCurrent(string repositoryRoot)
    {
        var projects = new List<PackageProject>();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        if (!Directory.Exists(sourceRoot))
            return projects;

        foreach (var projectPath in Directory.GetFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var document = XDocument.Load(projectPath);
            var packageId = ReadProperty(document, "PackageId");
            var version = ReadProperty(document, "Version");
            var isPackable = ReadProperty(document, "IsPackable");
            if (string.IsNullOrWhiteSpace(packageId) ||
                string.IsNullOrWhiteSpace(version) ||
                string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            projects.Add(new PackageProject(
                NormalizePath(projectPath),
                packageId,
                version,
                ReadProjectReferences(projectPath, document),
                ReadVersionSourceReferences(projectPath, document),
                ReadPackedInputPaths(projectPath, document)));
        }

        return projects;
    }

    public static IReadOnlyList<PackageProject> ReadAtGitRef(string repositoryRoot, string gitRef)
    {
        if (string.Equals(gitRef, "WORKTREE", StringComparison.Ordinal))
            return ReadCurrent(repositoryRoot);

        var projects = new List<PackageProject>();
        var projectList = GitRunner.Run(repositoryRoot, "ls-tree", "-r", "--name-only", gitRef, "src")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

        foreach (var relativeProjectPath in projectList)
        {
            var xml = GitRunner.Run(repositoryRoot, "show", $"{gitRef}:{relativeProjectPath}");
            var document = XDocument.Parse(xml);
            var packageId = ReadProperty(document, "PackageId");
            var version = ReadProperty(document, "Version");
            var isPackable = ReadProperty(document, "IsPackable");
            if (string.IsNullOrWhiteSpace(packageId) ||
                string.IsNullOrWhiteSpace(version) ||
                string.Equals(isPackable, "false", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var absoluteProjectPath = Path.Combine(repositoryRoot, relativeProjectPath);
            projects.Add(new PackageProject(
                NormalizePath(absoluteProjectPath),
                packageId,
                version,
                ReadProjectReferences(absoluteProjectPath, document),
                ReadVersionSourceReferences(absoluteProjectPath, document),
                ReadPackedInputPaths(absoluteProjectPath, document)));
        }

        return projects;
    }

    private static string? ReadProperty(XDocument document, string name)
    {
        return document.Root?
            .Elements("PropertyGroup")
            .Elements(name)
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);
    }

    private static IReadOnlyList<string> ReadProjectReferences(string projectPath, XDocument document)
    {
        return document.Root?
            .Elements("ItemGroup")
            .Elements("ProjectReference")
            .Where(element => !IsSuppressedProjectReference(element))
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolveMsBuildPath(Path.GetDirectoryName(projectPath)!, value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static bool IsSuppressedProjectReference(XElement element)
    {
        return string.Equals(element.Attribute("ReferenceOutputAssembly")?.Value, "false", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(element.Attribute("PrivateAssets")?.Value, "all", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadPackedInputPaths(string projectPath, XDocument document)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var itemNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Compile",
            "None",
            "Content",
            "EmbeddedResource"
        };

        return document.Root?
            .Elements("ItemGroup")
            .Elements()
            .Where(element => itemNames.Contains(element.Name.LocalName))
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolveMsBuildPath(projectDirectory, value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static IReadOnlyList<string> ReadVersionSourceReferences(string projectPath, XDocument document)
    {
        return document.Root?
            .Descendants("XmlPeek")
            .Where(element => string.Equals(element.Attribute("Query")?.Value, "/Project/PropertyGroup/Version/text()", StringComparison.Ordinal))
            .Select(element => element.Attribute("XmlInputPath")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value!.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Select(value => ResolveMsBuildProjectDirectory(projectPath, value!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static string ResolveMsBuildProjectDirectory(string projectPath, string value)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        return ResolveMsBuildPath(projectDirectory, value);
    }

    private static string ResolveMsBuildPath(string projectDirectory, string value)
    {
        var expanded = value
            .Replace("$(MSBuildProjectDirectory)", projectDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return NormalizePath(Path.GetFullPath(Path.Combine(projectDirectory, expanded)));
    }

    internal static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
