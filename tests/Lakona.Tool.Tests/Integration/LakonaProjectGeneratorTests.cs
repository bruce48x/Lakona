using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Execution;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Lakona.Tool.Rendering.Common;
using Lakona.Tool.Rendering.Docs;
using Lakona.Tool.Rendering.Operations;
using Lakona.Tool.Rendering.Project;
using Lakona.Tool.Rendering.Server;
using Lakona.Tool.Rendering.Shared;
using Xunit;

namespace Lakona.Tool.Tests.Integration;

public sealed class LakonaProjectGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_WritesPlanTransactionally()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-project-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Godot,
                TransportKind.WebSocket,
                SerializerKind.Json,
                PersistenceKind.None,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.Compose));
            var generator = new LakonaProjectGenerator(
                new LakonaProjectPlanBuilder(
                    [
                        new GitRenderer(),
                        new ProjectConfigRenderer(),
                        new SharedProjectRenderer(),
                        new ServerAppRenderer(),
                        new HotfixRenderer(),
                        new OperationsRenderer(),
                        new GeneratedProjectGuideRenderer()
                    ],
                    [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]),
                new GenerationExecutor(new TransactionalOutputWriter()),
                new GitInitializer(new GitUnavailableRunner()));

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);
            Assert.Equal(GitInitializationStatus.SkippedGitUnavailable, result.Git.Status);

            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Shared", "Shared.csproj")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Server", "App", "Server.App.csproj")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Client", "project.godot")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "docker-compose.cluster.yml")));
            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Server", "Server")));
            Assert.False(File.Exists(Path.Combine(spec.Layout.RootPath, "docs", "GETTING_STARTED.md")));
            Assert.False(File.Exists(Path.Combine(spec.Layout.RootPath, "docs", "EDITING_GUIDE.md")));
            Assert.False(File.Exists(Path.Combine(spec.Layout.RootPath, "docs", "OPERATIONS.md")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "README.md")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "AGENTS.md")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "CLAUDE.md")));
            Assert.Empty(Directory.GetDirectories(parentRoot, ".MyGame.tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("UnityCn")]
    [InlineData("Tuanjie")]
    public async Task GenerateAsync_UnityChinaFriendlyEngines_WriteEmbeddedNuGetForUnity(string engineName)
    {
        var engine = Enum.Parse<ClientEngine>(engineName);
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-project-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                engine,
                TransportKind.Kcp,
                SerializerKind.MemoryPack,
                PersistenceKind.None,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None,
                Presence: NewProjectOptionPresence.NuGetForUnitySource));
            var generator = new LakonaProjectGenerator(
                new LakonaProjectPlanBuilder(
                    [
                        new GitRenderer(),
                        new ProjectConfigRenderer(),
                        new SharedProjectRenderer(),
                        new ServerAppRenderer(),
                        new HotfixRenderer(),
                        new OperationsRenderer(),
                        new GeneratedProjectGuideRenderer()
                    ],
                    [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]),
                new GenerationExecutor(new TransactionalOutputWriter()),
                new GitInitializer(new GitUnavailableRunner()));

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);
            Assert.Equal(GitInitializationStatus.SkippedGitUnavailable, result.Git.Status);

            Assert.True(File.Exists(Path.Combine(
                spec.Layout.RootPath,
                "Client",
                "Packages",
                "com.github-glitchenzo.nugetforunity",
                "package.json")));
            var manifest = await File.ReadAllTextAsync(
                Path.Combine(spec.Layout.RootPath, "Client", "Packages", "manifest.json"),
                TestContext.Current.CancellationToken);
            Assert.DoesNotContain("package.openupm.com", manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("\"com.github-glitchenzo.nugetforunity\": \"4.5.0\"", manifest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_ConsoleClient_CreatesConsoleClientOnly()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "lakona-project-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parentRoot);
        try
        {
            var spec = new LakonaProjectSpecFactory().Create(new NewProjectOptions(
                "MyGame",
                parentRoot,
                ClientEngine.Console,
                TransportKind.Kcp,
                SerializerKind.MemoryPack,
                PersistenceKind.None,
                NuGetForUnitySource.OpenUpm,
                DeploymentProfile.None));
            var generator = new LakonaProjectGenerator(
                new LakonaProjectPlanBuilder(
                    [
                        new GitRenderer(),
                        new ProjectConfigRenderer(),
                        new SharedProjectRenderer(),
                        new ServerAppRenderer(),
                        new HotfixRenderer(),
                        new OperationsRenderer(),
                        new GeneratedProjectGuideRenderer()
                    ],
                    [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]),
                new GenerationExecutor(new TransactionalOutputWriter()),
                new GitInitializer(new GitUnavailableRunner()));

            var result = await generator.GenerateAsync(spec, TestContext.Current.CancellationToken);
            Assert.Equal(GitInitializationStatus.SkippedGitUnavailable, result.Git.Status);

            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Client", "Client.csproj")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Client", "Program.cs")));
            Assert.True(File.Exists(Path.Combine(spec.Layout.RootPath, "Client", "LoadScenarios", "LoginChatLoadScenario.cs")));
            Assert.False(Directory.Exists(Path.Combine(spec.Layout.RootPath, "Client", "Assets")));
            Assert.False(File.Exists(Path.Combine(spec.Layout.RootPath, "Client", "project.godot")));
        }
        finally
        {
            Directory.Delete(parentRoot, recursive: true);
        }
    }

    private sealed class GitUnavailableRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            string[] arguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new GitCommandResult(1, "", ""));
        }
    }
}
