using System.Text.Json;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Project;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ProjectConfigRendererTests
{
    [Fact]
    public void AddFiles_EmitsToolProjectConfigWithoutNetworkProfile()
    {
        var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "Arena",
            ".",
            ClientEngine.Godot,
            TransportKind.WebSocket,
            SerializerKind.Json,
            PersistenceKind.MySql,
            NuGetForUnitySource.Embedded,
            DeploymentProfile.Compose));
        var builder = new GenerationPlanBuilder("Root");

        new ProjectConfigRenderer().AddFiles(spec, builder);

        var file = Assert.Single(builder.Build().Files);
        Assert.Equal("lakona-game.tool.json", file.RelativePath);
        Assert.Equal(GeneratedFileKind.Json, file.Kind);
        using var document = JsonDocument.Parse(file.Content);
        var project = document.RootElement.GetProperty("project");
        Assert.Equal("Arena", project.GetProperty("name").GetString());
        Assert.Equal("godot", project.GetProperty("clientEngine").GetString());
        Assert.Equal("websocket", project.GetProperty("transport").GetString());
        Assert.Equal("json", project.GetProperty("serializer").GetString());
        Assert.Equal("mysql", project.GetProperty("persistence").GetString());
        Assert.Equal("embedded", project.GetProperty("nuGetForUnitySource").GetString());
        Assert.Equal("compose", project.GetProperty("deployProfile").GetString());
        Assert.DoesNotContain("networkProfile", file.Content, StringComparison.OrdinalIgnoreCase);
    }
}
