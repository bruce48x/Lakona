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
        var value = ReadProperty(propsPath, "LakonaBuildTag");
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (value.Length > 64 || value.Any(static character => !IsAsciiLetterOrDigit(character)))
            {
                throw new InvalidOperationException(
                    $"LakonaBuildTag in '{propsPath}' must contain 1 to 64 ASCII letters and digits.");
            }

            return value;
        }

        throw new InvalidOperationException(
            $"Could not resolve LakonaBuildTag for project '{fullProjectPath}'. " +
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

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9';
}
