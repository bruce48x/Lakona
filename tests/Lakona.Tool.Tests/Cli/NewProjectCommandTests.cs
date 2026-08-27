using Lakona.ProjectSystem;
using Lakona.Tool.Cli.Commands;
using Lakona.Tool.Cli.Options;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class NewProjectCommandTests
{
    [Fact]
    public async Task RunAsync_NonInteractive_DelegatesTypedRequest()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "lakona-new-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputRoot);
        try
        {
            var terminal = new FakeTerminal([], isInputRedirected: true);
            var creator = new FakeProjectCreator();
            var command = CreateCommand(terminal, creator);

            var exitCode = await command.RunAsync(
                [
                    "--name", "MyGame",
                    "--output", outputRoot,
                    "--client-engine", "godot",
                    "--transport", "websocket",
                    "--serializer", "json",
                    "--membership-provider", "mysql"
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(0, exitCode);
            AssertBuildAndStartStepsPrecedeHealthCheck(terminal.Output);
            var request = Assert.IsType<LakonaProjectCreationRequest>(creator.Request);
            Assert.Equal("MyGame", request.ProjectName);
            Assert.Equal(outputRoot, request.OutputPath);
            Assert.Equal(LakonaClientEngine.Godot, request.ClientEngine);
            Assert.Equal(LakonaTransport.WebSocket, request.Transport);
            Assert.Equal(LakonaSerializer.Json, request.Serializer);
            Assert.Equal(LakonaMembershipProvider.MySql, request.MembershipProvider);
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
        return CreateCommand(
            terminal,
            new FakeProjectCreator(),
            ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static NewProjectCommand CreateCommand(
        ICliTerminal terminal,
        ILakonaProjectCreator creator,
        ToolText? text = null)
    {
        text ??= ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture);
        return new NewProjectCommand(
            new NewProjectPrompter(text, terminal),
            creator,
            text,
            terminal);
    }

    private static NewProjectCommand CreateCommand(ICliTerminal terminal, ToolText text)
    {
        return CreateCommand(terminal, new FakeProjectCreator(), text);
    }

    private static void AssertBuildAndStartStepsPrecedeHealthCheck(IReadOnlyList<string> output)
    {
        var buildIndex = IndexOf(output, "  2) dotnet build \"Server/Server.slnx\"");
        var hotfixBuildIndex = IndexOf(output, "dotnet build \"Server/Hotfix/Server.Hotfix.csproj\"");
        var serverStartIndex = IndexOf(output, "  3) dotnet run --project \"Server/App/Server.App.csproj\" --no-build");
        var healthIndex = IndexOf(output, "  4) curl http://127.0.0.1:20080/_lakona/health/ready");
        var clientIndex = IndexOf(output, "  5) Open Client/ in Godot Engine");

        Assert.True(buildIndex >= 0, "Expected the generated next steps to include a server build step.");
        Assert.True(hotfixBuildIndex < 0, "Expected the solution build to replace the redundant hotfix build step.");
        Assert.True(serverStartIndex >= 0, "Expected the generated next steps to include a server start step.");
        Assert.True(healthIndex >= 0, "Expected the generated next steps to include a readiness endpoint check.");
        Assert.True(clientIndex >= 0, "Expected the generated next steps to include the renumbered client step.");
        Assert.True(buildIndex < serverStartIndex, "Expected the server build step to appear before server start.");
        Assert.True(serverStartIndex < healthIndex, "Expected the server start step to appear before the readiness endpoint check.");
        Assert.True(healthIndex < clientIndex, "Expected the readiness endpoint check to appear before the client step.");
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

    private sealed class FakeProjectCreator : ILakonaProjectCreator
    {
        public LakonaProjectCreationRequest? Request { get; private set; }

        public Task<LakonaProjectCreationResult> CreateAsync(
            LakonaProjectCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new LakonaProjectCreationResult(
                Path.Combine(request.OutputPath ?? ".", request.ProjectName ?? "MyGame"),
                LakonaGitInitializationStatus.SkippedGitUnavailable));
        }
    }
}
