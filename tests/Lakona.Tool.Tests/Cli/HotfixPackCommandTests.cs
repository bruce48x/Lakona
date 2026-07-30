using Lakona.ProjectSystem;
using Lakona.Tool.Cli.Commands.Hotfix;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class HotfixPackCommandTests
{
    [Fact]
    public async Task RunAsync_uses_server_build_as_default_output()
    {
        var packager = new FakeProjectPackager();
        var command = new HotfixPackCommand(new FakeTerminal(), packager);

        var exitCode = await command.RunAsync(
            ["--version", "v20260730-120000Z"],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.NotNull(packager.Request);
        Assert.Equal(LakonaPackageKind.Hotfix, packager.Request.Kind);
        Assert.Equal("Server/Build", packager.Request.OutputDirectory);
        Assert.Equal(
            Path.Combine("Server", "Hotfix", "Server.Hotfix.csproj"),
            packager.Request.HotfixProjectPath);
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
            return Task.FromResult(new LakonaPackageResult(
                request.Kind,
                Path.Combine(request.OutputDirectory!, $"Server.Hotfix-{request.Version}.zip"),
                request.RuntimeIdentifier,
                request.Configuration,
                request.Version!));
        }
    }

    private sealed class FakeTerminal : ICliTerminal
    {
        public bool IsInputRedirected => true;
        public bool IsOutputRedirected => false;

        public string? ReadLine() => null;

        public void Write(string value)
        {
        }

        public void WriteLine(string value)
        {
        }

        public void WriteErrorLine(string value)
        {
        }
    }
}
