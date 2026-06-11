using System.Text;

namespace Lakona.Tool.Domain;

internal sealed record ProjectLayout(
    string ProjectName,
    string OutputPath,
    string RootPath,
    string RootNamespace,
    string ServerProjectName,
    string SharedProjectName,
    string GodotAssemblyName,
    string UnityPackageId,
    string GeneratedDocsTitle)
{
    public static ProjectLayout Create(string projectName, string? outputPath)
    {
        var effectiveOutputPath = string.IsNullOrWhiteSpace(outputPath) ? Directory.GetCurrentDirectory() : outputPath;
        var safeName = string.IsNullOrWhiteSpace(projectName) ? "MyGame" : projectName;
        var asciiWords = GetAsciiWords(safeName);
        var compactName = string.Concat(asciiWords);
        if (string.IsNullOrEmpty(compactName))
        {
            compactName = "MyGame";
        }

        var rootNamespace = char.IsDigit(compactName[0]) ? $"_{compactName}" : compactName;
        var packageSuffix = string.Concat(compactName.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();
        if (string.IsNullOrEmpty(packageSuffix))
        {
            packageSuffix = "mygame";
        }

        return new ProjectLayout(
            safeName,
            effectiveOutputPath,
            Path.Combine(effectiveOutputPath, safeName),
            rootNamespace,
            "Server.App",
            "Shared",
            "Client",
            $"com.lakona.{packageSuffix}",
            $"Lakona {string.Join(" ", asciiWords)}");
    }

    private static IReadOnlyList<string> GetAsciiWords(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                current.Append(ch);
                continue;
            }

            AddCurrentWord(words, current);
        }

        AddCurrentWord(words, current);
        return words.Count == 0 ? ["MyGame"] : words;
    }

    private static void AddCurrentWord(List<string> words, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        words.Add(current.ToString());
        current.Clear();
    }
}
