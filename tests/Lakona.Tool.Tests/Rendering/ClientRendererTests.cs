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

    [Fact]
    public void UnityClientRenderer_OpenUpmManifest_IncludesNuGetForUnityRegistry()
    {
        var plan = Render(new UnityClientRenderer(), Spec(ClientEngine.Unity, NuGetForUnitySource.OpenUpm));
        var manifest = Assert.Single(plan.Files, file => file.RelativePath == "Client/Packages/manifest.json").Content;

        Assert.Contains("\"com.github-glitchenzo.nugetforunity\": \"4.5.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"scopedRegistries\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"OpenUPM\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"url\": \"https://package.openupm.com\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.github-glitchenzo.nugetforunity\"", manifest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UnityCn")]
    [InlineData("Tuanjie")]
    public void UnityClientRenderer_EmbeddedManifest_DoesNotReferenceOpenUpmOrNuGetForUnityPackage(string engineName)
    {
        var engine = Enum.Parse<ClientEngine>(engineName);
        var plan = Render(new UnityClientRenderer(), Spec(engine, NuGetForUnitySource.Embedded));
        var manifest = Assert.Single(plan.Files, file => file.RelativePath == "Client/Packages/manifest.json").Content;

        Assert.DoesNotContain("package.openupm.com", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("\"com.github-glitchenzo.nugetforunity\": \"4.5.0\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"dependencies\"", manifest, StringComparison.Ordinal);
        Assert.Contains("\"com.lakona.mygame.shared\": \"file:../../Shared\"", manifest, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UnityCn")]
    [InlineData("Tuanjie")]
    public void UnityClientRenderer_EmbeddedSource_RequestsNuGetForUnityArchiveExtraction(string engineName)
    {
        var engine = Enum.Parse<ClientEngine>(engineName);
        var plan = Render(new UnityClientRenderer(), Spec(engine, NuGetForUnitySource.OpenUpm));

        var archive = Assert.Single(plan.Archives ?? []);
        Assert.Equal("Lakona.Tool.Rendering.Client.TemplateAssets.NuGetForUnity.4.5.0.zip", archive.ResourceName);
        Assert.Equal("Client/Packages", archive.RelativeDestinationPath);
    }

    private static GenerationPlan Render(IClientRenderer renderer, LakonaProjectSpec spec)
    {
        var builder = new GenerationPlanBuilder("Root");
        renderer.AddFiles(spec, builder);
        return builder.Build();
    }

    private static LakonaProjectSpec Spec(ClientEngine engine, NuGetForUnitySource source = NuGetForUnitySource.OpenUpm)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            engine,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            PersistenceKind.None,
            source,
            DeploymentProfile.None));
    }
}
