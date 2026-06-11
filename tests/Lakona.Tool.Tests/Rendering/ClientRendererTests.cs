using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class ClientRendererTests
{
    [Fact]
    public void UnityClientRenderer_EmitsUnityFilesAndNoGodotFiles()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity));

        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Packages/manifest.json");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Assets/packages.config");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Assets/NuGet.config");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/ProjectSettings/ProjectVersion.txt");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.EndsWith(".tscn", StringComparison.Ordinal));
    }

    [Fact]
    public void GodotClientRenderer_EmitsGodotFilesAndNoUnityFiles()
    {
        var plan = Render(new GodotClientRenderer(), Spec(ClientEngine.Godot));

        Assert.Contains(plan.Files, file => file.RelativePath == "Client/project.godot");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Client.csproj");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Login.tscn");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Chat.tscn");
        Assert.Contains(plan.Files, file => file.RelativePath == "Client/Theme/LakonaTheme.tres");
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.StartsWith("Client/Assets/", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Files, file => file.Content.Contains("BuildUi", StringComparison.Ordinal));
    }

    private static GenerationPlan Render(IClientRenderer renderer, LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        renderer.AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(ClientEngine engine)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            engine,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
    }
}
