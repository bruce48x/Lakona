using System.Xml.Linq;

namespace Lakona.ProjectSystem.Packaging;

internal static class BuildTagReader
{
    public static string Read(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var fullProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(fullProjectPath)!;
        var propsPath = Path.GetFullPath(
            Path.Combine(projectDirectory, "..", "BuildTag.props"));
        var value = ReadProperty(propsPath, "LakonaHotfixBuildTag");
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"Could not resolve LakonaHotfixBuildTag for project '{fullProjectPath}'. " +
            $"Define the shared BuildTag.props at '{propsPath}'.");
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
