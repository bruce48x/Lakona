using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Lakona.ProjectSystem.Generation.Rendering.Common;
using Xunit;

namespace Lakona.ProjectSystem.Tests.Rendering;

public sealed class GitRendererTests
{
    [Fact]
    public void AddFiles_EmitsUnityGitFiles()
    {
        AssertGitFiles(ClientEngine.Unity,
            expectUnitySpecific: true,
            expectGodotSpecific: false,
            expectLargeGameAssets: true);
    }

    [Fact]
    public void AddFiles_EmitsTuanjieGitFiles()
    {
        AssertGitFiles(ClientEngine.Tuanjie,
            expectUnitySpecific: true,
            expectGodotSpecific: false,
            expectLargeGameAssets: true);
    }

    [Fact]
    public void AddFiles_EmitsGodotGitFiles()
    {
        AssertGitFiles(ClientEngine.Godot,
            expectUnitySpecific: false,
            expectGodotSpecific: true,
            expectLargeGameAssets: true);
    }

    [Fact]
    public void AddFiles_EmitsConsoleGitFiles()
    {
        AssertGitFiles(ClientEngine.Console,
            expectUnitySpecific: false,
            expectGodotSpecific: false,
            expectLargeGameAssets: false);
    }

    private static void AssertGitFiles(
        ClientEngine engine,
        bool expectUnitySpecific,
        bool expectGodotSpecific,
        bool expectLargeGameAssets)
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
        Assert.Equal(GeneratedFileKind.Text, gitattributes.Kind);
        Assert.Contains("* text=auto eol=lf", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.bat text eol=crlf", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.cs text eol=lf diff=csharp", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.csproj text eol=lf", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.props text eol=lf", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.targets text eol=lf", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.sln text eol=crlf", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.slnx text eol=lf", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.resx text eol=lf", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.dll binary", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.pdb binary", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.nupkg binary", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("*.snk binary", gitattributes.Content, StringComparison.Ordinal);
        Assert.Contains("Dockerfile text eol=lf", gitattributes.Content, StringComparison.Ordinal);

        // Unity and Tuanjie use Force Text assets and UnityYAMLMerge for the
        // two formats the engine documents as safe for semantic merging.
        Assert.Equal(expectUnitySpecific,
            gitattributes.Content.Contains("*.meta text eol=lf", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific,
            gitattributes.Content.Contains("*.unity text eol=lf merge=unityyamlmerge", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific,
            gitattributes.Content.Contains("*.prefab text eol=lf merge=unityyamlmerge", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific,
            gitattributes.Content.Contains("/Client/ProjectSettings/** text eol=lf", StringComparison.Ordinal));
        Assert.Equal(expectUnitySpecific,
            gitattributes.Content.Contains("*.anim text eol=lf", StringComparison.Ordinal));
        Assert.DoesNotContain("*.asset text", gitattributes.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("*.asset merge=unityyamlmerge", gitattributes.Content, StringComparison.Ordinal);

        // Godot's text scene/resource formats stay diffable; binary resource
        // formats are stored through LFS.
        Assert.Equal(expectGodotSpecific,
            gitattributes.Content.Contains("*.tscn text eol=lf", StringComparison.Ordinal));
        Assert.Equal(expectGodotSpecific,
            gitattributes.Content.Contains("*.tres text eol=lf", StringComparison.Ordinal));
        Assert.Equal(expectGodotSpecific,
            gitattributes.Content.Contains("*.res filter=lfs diff=lfs merge=lfs -text", StringComparison.Ordinal));
        Assert.Equal(expectGodotSpecific,
            gitattributes.Content.Contains("*.scn filter=lfs diff=lfs merge=lfs -text", StringComparison.Ordinal));
        Assert.Equal(expectGodotSpecific,
            gitattributes.Content.Contains("*.anim filter=lfs diff=lfs merge=lfs -text", StringComparison.Ordinal));

        Assert.Equal(expectLargeGameAssets,
            gitattributes.Content.Contains("*.fbx filter=lfs diff=lfs merge=lfs -text", StringComparison.Ordinal));
        Assert.Equal(expectLargeGameAssets,
            gitattributes.Content.Contains("*.png filter=lfs diff=lfs merge=lfs -text", StringComparison.Ordinal));
    }

    private static LakonaProjectSpec Spec(ClientEngine engine)
    {
        return new ProjectSpecTestFactory().Create(new ProjectSpecTestOptions(
            "MyGame",
            ".",
            engine,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            NuGetForUnitySource.OpenUpm,
            DeploymentProfile.None));
    }
}
