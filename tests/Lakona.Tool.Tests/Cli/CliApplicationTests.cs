using System.Globalization;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsync_Version_PrintsToolVersion()
    {
        var terminal = new FakeTerminal();
        var app = new CliApplication(ToolText.ForCulture(CultureInfo.InvariantCulture), terminal);

        var exitCode = await app.RunAsync(["version"]);

        Assert.Equal(0, exitCode);
        Assert.Equal(["0.15.0"], terminal.Output);
        Assert.Empty(terminal.Errors);
    }

    [Fact]
    public async Task RunAsync_Help_PrintsToolVersionAndVersionCommand()
    {
        var terminal = new FakeTerminal();
        var app = new CliApplication(ToolText.ForCulture(CultureInfo.InvariantCulture), terminal);

        var exitCode = await app.RunAsync(["help"]);

        Assert.Equal(0, exitCode);
        var output = string.Join('\n', terminal.Output);
        Assert.Contains("Lakona.Tool 0.15.0", output, StringComparison.Ordinal);
        Assert.Contains("lakona-tool version", output, StringComparison.Ordinal);
        Assert.Empty(terminal.Errors);
    }

    [Theory]
    [InlineData("version", false)]
    [InlineData("--version", false)]
    [InlineData("help", false)]
    [InlineData("--help", false)]
    [InlineData("-h", false)]
    [InlineData("new", true)]
    public void ShouldPrintBanner_SuppressesBannerForMetadataCommands(string command, bool expected)
    {
        Assert.Equal(expected, CliProgramBrandPolicy.ShouldPrintBanner([command]));
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
