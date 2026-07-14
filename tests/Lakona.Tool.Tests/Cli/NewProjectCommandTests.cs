using Lakona.ProjectSystem;
using Lakona.Tool.Cli.Commands;
using Lakona.Tool.Cli.Options;
using Lakona.Tool.Domain;
using Lakona.Tool.Execution;
using Lakona.Tool.Planning;
using Lakona.Tool.Rendering.Client;
using Lakona.Tool.Rendering.Common;
using Lakona.Tool.Rendering.Docs;
using Lakona.Tool.Rendering.Operations;
using Lakona.Tool.Rendering.Server;
using Lakona.Tool.Rendering.Shared;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class NewProjectCommandTests
{
    [Fact]
    public async Task RunAsync_NonInteractive_GeneratesProject()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "lakona-new-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);
        try
        {
            var terminal = new FakeTerminal([], isInputRedirected: true);
            var command = CreateCommand(terminal);

            var exitCode = await command.RunAsync(
                [
                    "--name", "MyGame",
                    "--output", outputRoot,
                    "--client-engine", "godot",
                    "--transport", "websocket",
                    "--serializer", "json"
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            AssertBuildStepsPrecedeHealthCheck(terminal.Output);
            Assert.False(File.Exists(Path.Combine(outputRoot, "MyGame", "lakona-game.tool.json")));
            Assert.True(File.Exists(Path.Combine(outputRoot, "MyGame", "Client", "project.godot")));
            Assert.False(Directory.Exists(Path.Combine(outputRoot, "MyGame", "Server", "Server")));
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_MissingRequiredOptions_ReturnsUsageError()
    {
        var terminal = new FakeTerminal([], isInputRedirected: true);
        var command = CreateCommand(terminal);

        var exitCode = await command.RunAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains(terminal.Errors, line => line.Contains("Missing required options", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ConsoleClient_PrintsConsoleSmokeStep()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "lakona-new-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);
        try
        {
            var terminal = new FakeTerminal([], isInputRedirected: true);
            var command = CreateCommand(terminal);

            var exitCode = await command.RunAsync(
                [
                    "--name", "MyGame",
                    "--output", outputRoot,
                    "--client-engine", "console",
                    "--transport", "kcp",
                    "--serializer", "memorypack"
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            Assert.Contains(terminal.Output, line => line.Contains("dotnet run --project \"Client/Client.csproj\" -- smoke", StringComparison.Ordinal));
            Assert.DoesNotContain(terminal.Output, line => line.Contains("Unity Hub", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_Unity63Client_PrintsSelectedUnityVersion()
    {
        var outputRoot = Path.Combine(
            Path.GetTempPath(),
            "lakona-new-command-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);
        try
        {
            var terminal = new FakeTerminal([], isInputRedirected: true);
            var command = CreateCommand(terminal);

            var exitCode = await command.RunAsync(
                [
                    "--name", "MyGame",
                    "--output", outputRoot,
                    "--client-engine", "unity",
                    "--client-engine-version", "6.3",
                    "--transport", "kcp",
                    "--serializer", "memorypack"
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            Assert.Contains(
                terminal.Output,
                line => line.Contains("Unity Hub (Unity 6.3)", StringComparison.Ordinal));
            var projectVersion = await File.ReadAllTextAsync(
                Path.Combine(
                    outputRoot,
                    "MyGame",
                    "Client",
                    "ProjectSettings",
                    "ProjectVersion.txt"),
                TestContext.Current.CancellationToken);
            Assert.Contains("m_EditorVersion: 6000.3.3f1", projectVersion, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_TuanjieClient_PrintsTuanjieOpenStep()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "lakona-new-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);
        try
        {
            var terminal = new FakeTerminal([], isInputRedirected: true);
            var command = CreateCommand(
                terminal,
                ToolText.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("zh-CN")));

            var exitCode = await command.RunAsync(
                [
                    "--name", "MyGame",
                    "--output", outputRoot,
                    "--client-engine", "tuanjie",
                    "--transport", "kcp",
                    "--serializer", "memorypack"
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            Assert.Contains(terminal.Output, line => line.Contains("用团结引擎打开 Client/ (团结 1.6.7)", StringComparison.Ordinal));
            Assert.DoesNotContain(terminal.Output, line => line.Contains("Unity Hub", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static NewProjectCommand CreateCommand(ICliTerminal terminal)
    {
        return CreateCommand(terminal, ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static NewProjectCommand CreateCommand(ICliTerminal terminal, ToolText text)
    {
        return new NewProjectCommand(
            new NewProjectPrompter(text, terminal),
            new LakonaProjectCreator(
                new ProjectSpecFactory(),
                new LakonaProjectGenerator(
                    new LakonaProjectPlanBuilder(
                        [
                            new GitRenderer(),
                            new SharedProjectRenderer(),
                            new ServerAppRenderer(),
                            new HotfixRenderer(),
                            new OperationsRenderer(),
                            new GeneratedProjectGuideRenderer()
                        ],
                        [new UnityClientRenderer(), new GodotClientRenderer(), new ConsoleClientRenderer()]),
                    new GenerationExecutor(new TransactionalOutputWriter()),
                    new GitInitializer(new GitUnavailableRunner()))),
            text,
            terminal);
    }

    private static void AssertBuildStepsPrecedeHealthCheck(IReadOnlyList<string> output)
    {
        var buildIndex = IndexOf(output, "dotnet build \"Server/Server.slnx\"");
        var hotfixBuildIndex = IndexOf(output, "dotnet build \"Server/Hotfix/Server.Hotfix.csproj\"");
        var serverStartIndex = IndexOf(output, "dotnet run --project \"Server/App/Server.App.csproj\" --no-build");
        var healthIndex = IndexOf(output, "/_lakona/health/ready");

        Assert.True(buildIndex >= 0, "Expected the generated next steps to include a server build step.");
        Assert.True(hotfixBuildIndex >= 0, "Expected the generated next steps to include a hotfix build step.");
        Assert.True(serverStartIndex >= 0, "Expected the generated next steps to include a server start step.");
        Assert.True(healthIndex >= 0, "Expected the generated next steps to include a readiness endpoint check.");
        Assert.True(buildIndex < hotfixBuildIndex, "Expected the server build step to appear before the hotfix build.");
        Assert.True(hotfixBuildIndex < serverStartIndex, "Expected the hotfix build to appear before server start.");
        Assert.True(serverStartIndex < healthIndex, "Expected the server start step to appear before the readiness endpoint check.");
    }

    private static int IndexOf(IReadOnlyList<string> output, string value)
    {
        for (var index = 0; index < output.Count; index++)
        {
            if (output[index].Contains(value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
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

    private sealed class FakeTerminal : ICliTerminal
    {
        private readonly Queue<string?> input;

        public FakeTerminal(IEnumerable<string?> input, bool isInputRedirected = false)
        {
            this.input = new Queue<string?>(input);
            IsInputRedirected = isInputRedirected;
        }

        public bool IsInputRedirected { get; }
        public bool IsOutputRedirected => false;
        public List<string> Output { get; } = [];
        public List<string> Errors { get; } = [];

        public string? ReadLine() => input.Count > 0 ? input.Dequeue() : null;

        public void Write(string value)
        {
            Output.Add(value);
        }

        public void WriteLine(string value)
        {
            Output.Add(value);
        }

        public void WriteErrorLine(string value)
        {
            Errors.Add(value);
        }
    }
}
