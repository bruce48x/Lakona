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
        AssertGitFiles(ClientEngine.Unity,
            expectUnitySpecific: true,
            expectGodotSpecific: false);
    }

    [Fact]
    public void AddFiles_EmitsTuanjieGitFiles()
    {
        AssertGitFiles(ClientEngine.Tuanjie,
            expectUnitySpecific: true,
            expectGodotSpecific: false);
    }

    [Fact]
    public void AddFiles_EmitsGodotGitFiles()
    {
        AssertGitFiles(ClientEngine.Godot,
            expectUnitySpecific: false,
            expectGodotSpecific: true);
    }

    [Fact]
    public void AddFiles_EmitsConsoleGitFiles()
    {
        AssertGitFiles(ClientEngine.Console,
            expectUnitySpecific: false,
            expectGodotSpecific: false);
    }

    private static void AssertGitFiles(ClientEngine engine, bool expectUnitySpecific, bool expectGodotSpecific)
    {
        var builder = new GenerationPlanBuilder("Root");
        var spec = Spec(engine);

        new GitRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var gitignore = Assert.Single(plan.Files, file => file.RelativePath == ".gitignore");
        Assert.Equal(GeneratedFileKind.Text, gitignore.Kind);

        // Common ignores — all engines
        Assert.Contains("**/bin/", gitignore.Content, StringComparison.Ordinal);
        Assert.Contains("**/obj/", gitignore.Content, StringComparison.Ordinal);
        Assert.Contains("/_artifacts/", gitignore.Content, StringComparison.Ordinal);
        Assert.Contains(".vs/", gitignore.Content, StringComparison.Ordinal);
        Assert.Contains(".idea/", gitignore.Content, StringComparison.Ordinal);
        Assert.Contains("*.user", gitignore.Content, StringComparison.Ordinal);
        Assert.Contains("*.suo", gitignore.Content, StringComparison.Ordinal);

        // Unity-specific
        Assert.Equal(expectUnitySpecific, gitignore.Content.Contains("/Client/[Ll]ibrary/", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific, gitignore.Content.Contains("/Client/[Tt]emp/", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific, gitignore.Content.Contains("/Client/[Oo]bj/", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific, gitignore.Content.Contains("/Client/[Bb]uild/", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific, gitignore.Content.Contains("/Client/[Bb]uilds/", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific, gitignore.Content.Contains("/Client/[Ll]ogs/", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific, gitignore.Content.Contains("/Client/[Uu]ser[Ss]ettings/", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific, gitignore.Content.Contains("/Client/Assets/Packages/", StringComparison.Ordinal));

        // Godot-specific
        Assert.Equal(expectGodotSpecific, gitignore.Content.Contains("/Client/.godot/", StringComparison.Ordinal));
        Assert.Equal(expectGodotSpecific, gitignore.Content.Contains("/Client/.import/", StringComparison.Ordinal));

        // Gitattributes always present
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
