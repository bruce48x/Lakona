using System.Reflection;
using System.Text;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Rendering.Docs;

internal sealed class AgentSkillsRenderer : IPlanContributor
{
    private const string ResourcePrefix = "Lakona.ProjectSystem.SkillPack/";
    private const string ProjectSkillRoot = ".agents/skills/";

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        var assembly = typeof(AgentSkillsRenderer).Assembly;
        var resources = assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
        {
            throw new InvalidOperationException("The embedded Lakona Skill Pack is empty.");
        }

        foreach (var resourceName in resources)
        {
            var relativePath = resourceName[ResourcePrefix.Length..].Replace('\\', '/');
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded Lakona Skill file not found: {resourceName}");
            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            builder.AddFile(
                ProjectSkillRoot + relativePath,
                reader.ReadToEnd(),
                FileWriteMode.Replace,
                FileKind(relativePath));
        }
    }

    private static GeneratedFileKind FileKind(string relativePath) =>
        Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".md" => GeneratedFileKind.Markdown,
            ".json" => GeneratedFileKind.Json,
            ".xml" => GeneratedFileKind.Xml,
            _ => GeneratedFileKind.Text
        };
}
