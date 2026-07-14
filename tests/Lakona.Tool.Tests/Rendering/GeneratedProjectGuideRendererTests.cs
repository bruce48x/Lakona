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
        Assert.Contains("server-pushed world snapshots", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("polled world snapshots", readme.Content, StringComparison.Ordinal);
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
        Assert.Contains("| Client engine version | 2022 |", readme.Content, StringComparison.Ordinal);
        Assert.Contains("| Transport | kcp |", readme.Content, StringComparison.Ordinal);
        Assert.Contains("| Serializer | memorypack |", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_ReflectsSelectedUnityVersion()
    {
        var spec = Spec(
            ClientEngine.Unity,
            TransportKind.Kcp,
            SerializerKind.MemoryPack,
            DeploymentProfile.None,
            ClientEngineVersion.Unity63);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var readme = Assert.Single(builder.Build().Files, file => file.RelativePath == "README.md");
        Assert.Contains("Unity 6.3 client using UI Toolkit", readme.Content, StringComparison.Ordinal);
        Assert.Contains("| Client engine version | 6.3 |", readme.Content, StringComparison.Ordinal);
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
        Assert.Contains("public GameService(GameWorldActors worlds, ILakonaGameServer gameServer)", readme.Content, StringComparison.Ordinal);
        Assert.Contains("await _worlds.Startup(GameWorldIds.Global).CallAsync(", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("starterNodeLocalActors.AskAsync", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(".AskAsync", readme.Content, StringComparison.Ordinal);
        Assert.Contains("RPC services that target actors whose", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("starterLocalActors", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("localActors.AskAsync", readme.Content, StringComparison.Ordinal);
        Assert.Contains("rooms.Route(roomId).CallAsync", readme.Content, StringComparison.Ordinal);
        Assert.Contains("rooms.Local(roomId)", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("rooms.Remote(nodeId, roomId)", readme.Content, StringComparison.Ordinal);
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
        Assert.Contains("Server/Hotfix/ Reloadable services, actor behaviors, actor startup, timer callbacks", readme.Content, StringComparison.Ordinal);
        Assert.Contains("hotfix actor startup path ensures the fixed local `GameWorldActor` exists", readme.Content, StringComparison.Ordinal);
        Assert.Contains("Killing a player awards half", readme.Content, StringComparison.Ordinal);
        Assert.Contains("No external art files are included", readme.Content, StringComparison.Ordinal);
        Assert.Contains("Lakona:Hotfix:DebugWatcher=On", readme.Content, StringComparison.Ordinal);
        Assert.Contains("reload.signal", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("stable Server/App service binding", readme.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("actor state, host binding, runtime integration", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DistinguishesInitialServerPackageFromHotfixPackage()
    {
        var spec = Spec(ClientEngine.Console, TransportKind.Kcp, SerializerKind.MemoryPack,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        Assert.Contains("lakona-tool server pack --runtime linux-x64", readme.Content, StringComparison.Ordinal);
        Assert.Contains("initial deployable server zip", readme.Content, StringComparison.Ordinal);
        Assert.Contains("lakona-tool hotfix pack", readme.Content, StringComparison.Ordinal);
        Assert.Contains("future hotfix zips", readme.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_BuildsAndStartsBeforeHealthCheck()
    {
        var spec = Spec(ClientEngine.Godot, TransportKind.WebSocket, SerializerKind.Json,
            DeploymentProfile.None);
        var builder = new GenerationPlanBuilder("Root");

        new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

        var plan = builder.Build();
        var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
        var buildIndex = readme.Content.IndexOf("dotnet build \"Server/Server.slnx\"", StringComparison.Ordinal);
        var hotfixBuildIndex = readme.Content.IndexOf("dotnet build \"Server/Hotfix/Server.Hotfix.csproj\"", StringComparison.Ordinal);
        var serverStartIndex = readme.Content.IndexOf("dotnet run --project \"Server/App/Server.App.csproj\" --no-build", StringComparison.Ordinal);
        var readinessIndex = readme.Content.IndexOf("/_lakona/health/ready", StringComparison.Ordinal);

        Assert.True(buildIndex >= 0, "Expected the generated README to include a server build command.");
        Assert.True(hotfixBuildIndex >= 0, "Expected the generated README to include an explicit hotfix build command.");
        Assert.True(serverStartIndex >= 0, "Expected the generated README to include a server start command.");
        Assert.True(readinessIndex >= 0, "Expected the generated README to include a readiness endpoint check.");
        Assert.True(buildIndex < hotfixBuildIndex, "Expected the generated README to build the server before hotfix output.");
        Assert.True(hotfixBuildIndex < serverStartIndex, "Expected the generated README to build hotfix output before starting the server.");
        Assert.True(serverStartIndex < readinessIndex, "Expected the generated README to start the server before checking readiness.");
    }

    private static LakonaProjectSpec Spec(ClientEngine engine, TransportKind transport,
        SerializerKind serializer, DeploymentProfile deploy,
        ClientEngineVersion? version = null)
    {
        return new LakonaProjectSpecFactory().Create(new NewProjectOptions(
            "MyGame",
            ".",
            engine,
            transport,
            serializer,
            PersistenceKind.None,
            NuGetForUnitySource.OpenUpm,
            deploy,
            ClientEngineVersion: version));
    }
}
