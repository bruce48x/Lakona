using Lakona.Tool.Cli.Commands.Server;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class ServerPackCommandTests
{
    [Fact]
    public async Task RunAsync_requires_runtime()
    {
        var command = new ServerPackCommand(new FakeTerminal(), new FakeProjectPackager());

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            () => command.RunAsync([], TestContext.Current.CancellationToken));

        Assert.Contains("--runtime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_passes_defaults_and_configuration_to_writer()
    {
        var terminal = new FakeTerminal();
        var packager = new FakeProjectPackager();
        var command = new ServerPackCommand(terminal, packager);

        var exitCode = await command.RunAsync(
            [
                "--runtime", "linux-x64",
                "--configuration", "Debug",
                "--version", "v20260624-120000Z"
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.NotNull(packager.Request);
        Assert.Equal(LakonaPackageKind.Server, packager.Request.Kind);
        Assert.Equal("Server/App/Server.App.csproj", packager.Request.ServerProjectPath);
        Assert.Equal("Server/Hotfix/Server.Hotfix.csproj", packager.Request.HotfixProjectPath);
        Assert.Equal("artifacts/server", packager.Request.OutputDirectory);
        Assert.Equal("linux-x64", packager.Request.RuntimeIdentifier);
        Assert.Equal("Debug", packager.Request.Configuration);
        Assert.Equal("v20260624-120000Z", packager.Request.Version);
        Assert.Contains(
            terminal.Output,
            line => line.Contains("Packed server", StringComparison.Ordinal) &&
                line.Contains("Server.App-v20260624-120000Z-linux-x64.zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_rejects_unknown_option()
    {
        var command = new ServerPackCommand(new FakeTerminal(), new FakeProjectPackager());

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            () => command.RunAsync(["--runtime", "linux-x64", "--trim", "true"], TestContext.Current.CancellationToken));

        Assert.Contains("--trim", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerCommand_routes_pack_and_rejects_unknown_subcommand()
    {
        var terminal = new FakeTerminal();
        var packager = new FakeProjectPackager();
        var command = new ServerCommand(terminal, packager);

        var exitCode = await command.RunAsync(["pack", "--runtime", "linux-x64"], TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.NotNull(packager.Request);
        Assert.Equal("linux-x64", packager.Request.RuntimeIdentifier);

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            () => command.RunAsync(["publish"], TestContext.Current.CancellationToken));
        Assert.Contains("Unknown server subcommand 'publish'.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CliApplication_routes_server_command()
    {
        var terminal = new FakeTerminal();
        var application = new CliApplication(terminal: terminal);

        var exitCode = await application.RunAsync(["server"]);

        Assert.Equal(1, exitCode);
        Assert.Contains(
            terminal.Errors,
            line => line.Contains("Missing server subcommand", StringComparison.Ordinal));
    }

    private sealed class FakeProjectPackager : ILakonaProjectPackager
    {
        public LakonaPackageRequest? Request { get; private set; }

        public Task<LakonaPackageResult> PackAsync(
            LakonaPackageRequest request,
            IProgress<LakonaPackageProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            var artifactPath = Path.Combine(
                request.OutputDirectory!,
                $"Server.App-{request.Version}-{request.RuntimeIdentifier}.zip");
            return Task.FromResult(new LakonaPackageResult(
                request.Kind,
                artifactPath,
                request.RuntimeIdentifier,
                request.Configuration,
                request.Version!));
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
