using Lakona.Tool.Cli.Commands;
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
            var command = CreateCommand(new FakeTerminal([], isInputRedirected: true));

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
            Assert.True(File.Exists(Path.Combine(outputRoot, "MyGame", "lakona-game.tool.json")));
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

    private static NewProjectCommand CreateCommand(ICliTerminal terminal)
    {
        return new NewProjectCommand(
            new NewProjectPrompter(ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture), terminal),
            new LakonaProjectSpecFactory(),
            new LakonaProjectGenerator(
                new LakonaProjectPlanBuilder(
                    [
                        new GitRenderer(),
                        new ProjectConfigRenderer(),
                        new SharedProjectRenderer(),
                        new ServerAppRenderer(),
                        new HotfixRenderer(),
                        new OperationsRenderer(),
                        new GeneratedProjectDocsRenderer()
                    ],
                    [new UnityClientRenderer(), new GodotClientRenderer()]),
                new GenerationExecutor(new TransactionalOutputWriter())),
            ToolText.ForCulture(System.Globalization.CultureInfo.InvariantCulture),
            terminal);
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
        public List<string> Errors { get; } = [];

        public string? ReadLine() => input.Count > 0 ? input.Dequeue() : null;

        public void Write(string value)
        {
        }

        public void WriteLine(string value)
        {
        }

        public void WriteErrorLine(string value)
        {
            Errors.Add(value);
        }
    }
}
