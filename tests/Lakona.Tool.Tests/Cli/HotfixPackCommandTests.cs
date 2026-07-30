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
            [],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.NotNull(packager.Request);
        Assert.Equal(LakonaPackageKind.Hotfix, packager.Request.Kind);
        Assert.Equal("Server/Build", packager.Request.OutputDirectory);
        Assert.Equal(
            Path.Combine("Server", "Hotfix", "Server.Hotfix.csproj"),
            packager.Request.HotfixProjectPath);
    }

    [Fact]
    public async Task RunAsync_rejects_manual_package_versions()
    {
        var command = new HotfixPackCommand(new FakeTerminal(), new FakeProjectPackager());

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            () => command.RunAsync(
                ["--version", "manual"],
                TestContext.Current.CancellationToken));

        Assert.Contains("--version", exception.Message, StringComparison.Ordinal);
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
                Path.Combine(request.OutputDirectory!, "Server.Hotfix-Release1-20260730-120000Z.zip"),
                request.RuntimeIdentifier,
                request.Configuration,
                "20260730-120000Z"));
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
