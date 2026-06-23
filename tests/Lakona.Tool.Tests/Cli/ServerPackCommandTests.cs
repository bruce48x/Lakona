using Lakona.Tool.Cli.Commands.Server;
using Lakona.Tool.Server;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class ServerPackCommandTests
{
    [Fact]
    public async Task RunAsync_requires_runtime()
    {
        var command = new ServerPackCommand(new FakeTerminal(), new FakeServerPackageWriter());

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            () => command.RunAsync([], TestContext.Current.CancellationToken));

        Assert.Contains("--runtime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_passes_defaults_and_configuration_to_writer()
    {
        var terminal = new FakeTerminal();
        var writer = new FakeServerPackageWriter();
        var command = new ServerPackCommand(terminal, writer);

        var exitCode = await command.RunAsync(
            [
                "--runtime", "linux-x64",
                "--configuration", "Debug",
                "--version", "v20260624-120000Z"
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.NotNull(writer.Options);
        Assert.Equal("Server/App/Server.App.csproj", writer.Options.ProjectPath);
        Assert.Equal("Server/Hotfix/Server.Hotfix.csproj", writer.Options.HotfixProjectPath);
        Assert.Equal("artifacts/server", writer.Options.OutputDirectory);
        Assert.Equal("linux-x64", writer.Options.RuntimeIdentifier);
        Assert.Equal("Debug", writer.Options.Configuration);
        Assert.Equal("v20260624-120000Z", writer.Options.Version);
        Assert.Contains(
            terminal.Output,
            line => line.Contains("Packed server", StringComparison.Ordinal) &&
                line.Contains("Server.App-v20260624-120000Z-linux-x64.zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_rejects_unknown_option()
    {
        var command = new ServerPackCommand(new FakeTerminal(), new FakeServerPackageWriter());

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            () => command.RunAsync(["--runtime", "linux-x64", "--trim", "true"], TestContext.Current.CancellationToken));

        Assert.Contains("--trim", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerCommand_routes_pack_and_rejects_unknown_subcommand()
    {
        var terminal = new FakeTerminal();
        var writer = new FakeServerPackageWriter();
        var command = new ServerCommand(terminal, writer);

        var exitCode = await command.RunAsync(["pack", "--runtime", "linux-x64"], TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.NotNull(writer.Options);
        Assert.Equal("linux-x64", writer.Options.RuntimeIdentifier);

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            () => command.RunAsync(["publish"], TestContext.Current.CancellationToken));
        Assert.Contains("Unknown server subcommand 'publish'.", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeServerPackageWriter : IServerPackageWriter
    {
        public ServerPackOptions? Options { get; private set; }

        public Task<string> PackAsync(ServerPackOptions options, CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.FromResult(Path.Combine(options.OutputDirectory, $"Server.App-{options.Version}-{options.RuntimeIdentifier}.zip"));
        }
    }

    private sealed class FakeTerminal : ICliTerminal
    {
        public bool IsInputRedirected => true;
        public bool IsOutputRedirected => false;
        public List<string> Output { get; } = [];
        public List<string> Errors { get; } = [];

        public string? ReadLine() => null;

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
