using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Common;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class GitRendererTests
{
    [Fact]
    public void AddFiles_EmitsUnityGitFiles()
    {
        AssertGitFiles(ClientEngine.Unity, includesUnityLibrary: true);
    }

    [Fact]
    public void AddFiles_EmitsGodotGitFiles()
    {
        AssertGitFiles(ClientEngine.Godot, includesUnityLibrary: false);
    }

    private static void AssertGitFiles(ClientEngine engine, bool includesUnityLibrary)
    {
        var builder = new GenerationPlanBuilder("Root");
        var spec = Spec(engine);

        new GitRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var gitignore = Assert.Single(plan.Files, file => file.RelativePath == ".gitignore");
        Assert.Equal(GeneratedFileKind.Text, gitignore.Kind);
        Assert.Contains("**/bin/", gitignore.Content, StringComparison.Ordinal);
        Assert.Equal(includesUnityLibrary, gitignore.Content.Contains("/Client/[Ll]ibrary/", StringComparison.Ordinal));

        var gitattributes = Assert.Single(plan.Files, file => file.RelativePath == ".gitattributes");
        Assert.Contains("* text=auto", gitattributes.Content, StringComparison.Ordinal);
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
