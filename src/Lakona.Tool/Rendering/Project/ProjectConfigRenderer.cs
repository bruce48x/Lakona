using System.Text.Json;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Rendering.Project;

internal sealed class ProjectConfigRenderer : IPlanContributor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void AddFiles(LakonaProjectSpec spec, GenerationPlanBuilder builder)
    {
        var config = new GeneratedToolConfig(
            new GeneratedProjectConfig(
                spec.Name,
                ToolEnumText.ToCliValue(spec.ClientEngine),
                ToolEnumText.ToCliValue(spec.Transport),
                ToolEnumText.ToCliValue(spec.Serializer),
                ToolEnumText.ToCliValue(spec.Persistence),
                ToolEnumText.ToCliValue(spec.NuGetForUnitySource),
                ToolEnumText.ToCliValue(spec.DeploymentProfile)));

        builder.AddFile(
            "lakona-game.tool.json",
            JsonSerializer.Serialize(config, JsonOptions),
            FileWriteMode.Replace,
            GeneratedFileKind.Json);
    }

    private sealed record GeneratedToolConfig(GeneratedProjectConfig Project);

    private sealed record GeneratedProjectConfig(
        string Name,
        string ClientEngine,
        string Transport,
        string Serializer,
        string Persistence,
        string NuGetForUnitySource,
        string DeployProfile);
}
