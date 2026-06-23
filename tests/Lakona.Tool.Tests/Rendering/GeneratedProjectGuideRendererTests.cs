using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Docs;
using Xunit;

namespace Lakona.Tool.Tests.Rendering;

public sealed class GeneratedProjectGuideRendererTests
{
    [Fact]
    public void AddFiles_EmitsRootGuideFiles()
    {
        var spec = Spec(ClientEngine.Godot, TransportKind.WebSocket, SerializerKind.Json,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        Assert.Contains(plan.Files, file => file.RelativePath == "README.md");
        Assert.Contains(plan.Files, file => file.RelativePath == "AGENTS.md");
        Assert.Contains(plan.Files, file => file.RelativePath == "CLAUDE.md");
    }

    [Fact]
    public void AddFiles_DoesNotEmitDocsFiles()
    {
        var spec = Spec(ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        Assert.DoesNotContain(plan.Files, file => file.RelativePath.StartsWith("docs/", StringComparison.Ordinal));
    }

    [Fact]
    public void AgentsMd_PointsToReadme()
    {
        var spec = Spec(ClientEngine.Godot, TransportKind.WebSocket, SerializerKind.Json,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var agents = Assert.Single(plan.Files, file => file.RelativePath == "AGENTS.md");
        Assert.Contains("[README.md](README.md)", agents.Content, StringComparison.Ordinal);
        Assert.Contains("# Agent Instructions", agents.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeMd_PointsToReadme()
    {
        var spec = Spec(ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var claude = Assert.Single(plan.Files, file => file.RelativePath == "CLAUDE.md");
        Assert.Contains("[README.md](README.md)", claude.Content, StringComparison.Ordinal);
        Assert.Contains("# Claude Instructions", claude.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_H1UsesProjectName()
    {
        var spec = Spec(ClientEngine.Godot, TransportKind.WebSocket, SerializerKind.Json,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.StartsWith("# MyGame", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_OptionsTableReflectsSpec()
    {
        var spec = Spec(ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.Contains("| Client engine | unity |", readme.Content, StringComparison.Ordinal);
        Assert.Contains("| Transport | kcp |", readme.Content, StringComparison.Ordinal);
        Assert.Contains("| Serializer | memorypack |", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_WebSocketListenerText()
    {
        var spec = Spec(ClientEngine.Godot, TransportKind.WebSocket, SerializerKind.Json,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.Contains("ws://127.0.0.1:20000/ws", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_KcpListenerText()
    {
        var spec = Spec(ClientEngine.Console, TransportKind.Kcp, SerializerKind.MemoryPack,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.Contains("kcp://127.0.0.1:20000", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_ConsoleEngineDoesNotMentionUnityOrGodotAssets()
    {
        var spec = Spec(ClientEngine.Console, TransportKind.Kcp, SerializerKind.MemoryPack,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.DoesNotContain("Unity 2022.3", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Godot", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_ComposeTextWhenComposeSelected()
    {
        var spec = Spec(ClientEngine.Godot, TransportKind.WebSocket, SerializerKind.Json,
            DeploymentProfile.Compose);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.Contains("ops/", readme.Content, StringComparison.Ordinal);
        Assert.Contains("compose", readme.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_NoComposeTextWhenComposeNotSelected()
    {
        var spec = Spec(ClientEngine.Godot, TransportKind.Kcp, SerializerKind.MemoryPack,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.DoesNotContain("ops/", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_ExplainsLocalAndDistributedActorCalls()
    {
        var spec = Spec(ClientEngine.Unity, TransportKind.Kcp, SerializerKind.MemoryPack,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.Contains("node-local actor runtime", readme.Content, StringComparison.Ordinal);
        Assert.Contains("starterNodeLocalActors.AskAsync", readme.Content, StringComparison.Ordinal);
        Assert.Contains("This is not a remote actor call", readme.Content, StringComparison.Ordinal);
        Assert.Contains("RPC services that target actors whose", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", readme.Content, StringComparison.Ordinal);
        Assert.Contains("rooms.Get(roomId)", readme.Content, StringComparison.Ordinal);
        Assert.Contains("rooms.Local(roomId)", readme.Content, StringComparison.Ordinal);
        Assert.Contains("rooms.Remote(nodeId, roomId)", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DescribesAppAsStableHostAndHotfixAsBusinessLayer()
    {
        var spec = Spec(ClientEngine.Godot, TransportKind.WebSocket, SerializerKind.Json,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.Contains("Server/App/    Stable server host, actor state shells, configuration", readme.Content, StringComparison.Ordinal);
        Assert.Contains("Server/Hotfix/ Reloadable services, actor behaviors, lifecycle reactions, feature declarations", readme.Content, StringComparison.Ordinal);
        Assert.Contains("Hotfix feature declaration ensures the fixed local ChatRoomActor exists", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("stable Server/App service binding", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("actor state, host binding, runtime integration", readme.Content, StringComparison.Ordinal);
    }

    private static LakonaProjectSpec Spec(ClientEngine engine, TransportKind transport,
        SerializerKind serializer, DeploymentProfile deploy)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            engine,
            transport,
            serializer,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            deploy));
    }
}
