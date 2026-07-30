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

        throw new InvalidOperationException(
            $"Could not resolve LakonaHotfixBuildTag for project '{fullProjectPath}'. " +
            "Define BuildTag.props beside Server.App.csproj.");
    }

    private static IEnumerable<string> PropsCandidates(string projectDirectory)
    {
        yield return Path.Combine(projectDirectory, "BuildTag.props");
        yield return Path.GetFullPath(Path.Combine(projectDirectory, "..", "App", "BuildTag.props"));
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

}
