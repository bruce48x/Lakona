using System.Xml.Linq;

namespace Lakona.ProjectSystem.Packaging;

internal static class BuildTagReader
{
    public static string Read(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var fullProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)!;

        foreach (var propsPath in PropsCandidates(projectDirectory))
        {
            var value = ReadProperty(propsPath, "LakonaHotfixBuildTag");
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        foreach (var candidateProject in ProjectCandidates(fullProjectPath, projectDirectory))
        {
            var value = ReadInlineAssemblyMetadata(candidateProject);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException(
            $"Could not resolve LakonaHotfixBuildTag for project '{fullProjectPath}'. " +
            "Define BuildTag.props beside Server.App.csproj or declare the " +
            "LakonaHotfixBuildTag AssemblyMetadata attribute in Server.App.csproj.");
    }

    private static IEnumerable<string> PropsCandidates(string projectDirectory)
    {
        yield return Path.Combine(projectDirectory, "BuildTag.props");
        yield return Path.GetFullPath(Path.Combine(projectDirectory, "..", "App", "BuildTag.props"));
    }

    private static IEnumerable<string> ProjectCandidates(
        string projectPath,
        string projectDirectory)
    {
        yield return projectPath;
        yield return Path.GetFullPath(Path.Combine(projectDirectory, "..", "App", "Server.App.csproj"));
    }

    private static string? ReadProperty(string path, string propertyName)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return XDocument.Load(path)
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == propertyName)
            ?.Value
            .Trim();
    }

    private static string? ReadInlineAssemblyMetadata(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return null;
        }

        var document = XDocument.Load(projectPath);
        foreach (var attribute in document
                     .Descendants()
                     .Where(element =>
                         element.Name.LocalName == "AssemblyAttribute" &&
                         string.Equals(
                             element.Attribute("Include")?.Value,
                             "System.Reflection.AssemblyMetadataAttribute",
                             StringComparison.Ordinal)))
        {
            var key = attribute
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "_Parameter1")
                ?.Value;
            if (!string.Equals(key, "LakonaHotfixBuildTag", StringComparison.Ordinal))
            {
                continue;
            }

            return attribute
                .Elements()
                .FirstOrDefault(element => element.Name.LocalName == "_Parameter2")
                ?.Value
                .Trim();
        }

        return null;
    }
}
