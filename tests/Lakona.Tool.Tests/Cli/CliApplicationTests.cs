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
        Assert.Equal([ToolVersion.Current], terminal.Output);
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
        Assert.Contains($"Lakona.Tool {ToolVersion.Current}", output, StringComparison.Ordinal);
        Assert.Contains("lakona-tool version", output, StringComparison.Ordinal);
        Assert.Empty(terminal.Errors);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void HelpText_ListsAllSupportedCommandsAndOptions(string cultureName)
    {
        var help = ToolText.ForCulture(CultureInfo.GetCultureInfo(cultureName)).HelpText("test");

        var expectedEntries = new[]
        {
            "lakona-tool new",
            "lakona-tool init",
            "lakona-tool server pack",
            "lakona-tool hotfix pack",
            "lakona-tool hotfix install",
            "lakona-tool hotfix activate",
            "lakona-tool hotfix status",
            "lakona-tool hotfix rollback",
            "lakona-tool version",
            "--version",
            "--help",
            "-h",
            "--name",
            "--client-engine",
            "--client-engine-version",
            "--transport",
            "--serializer",
            "--membership-provider",
            "--nugetforunity-source",
            "--deploy-profile",
            "--runtime",
            "--configuration",
            "--output",
            "--project",
            "--hotfix-project",
            "--root",
            "--server",
            "--expected-current-version",
            "embedded",
            "compose",
            "1.6.7",
            "4.6",
            "https://github.com/bruce48x/Lakona/blob/main/docs/configuration.md"
        };

        foreach (var entry in expectedEntries)
        {
            Assert.Contains(entry, help, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RunAsync_NestedHelpRequests_PrintCompleteHelp()
    {
        var requests = new[]
        {
            new[] { "new", "--help" },
            new[] { "server", "--help" },
            new[] { "server", "pack", "--help" },
            new[] { "hotfix", "--help" },
            new[] { "hotfix", "activate", "--help" }
        };

        foreach (var request in requests)
        {
            var terminal = new FakeTerminal();
            var app = new CliApplication(ToolText.ForCulture(CultureInfo.InvariantCulture), terminal);

            var exitCode = await app.RunAsync(request);

            Assert.Equal(0, exitCode);
            Assert.Contains(
                terminal.Output,
                line => line.Contains("lakona-tool hotfix rollback", StringComparison.Ordinal));
            Assert.Empty(terminal.Errors);
        }
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

    [Fact]
    public void ShouldPrintBanner_SuppressesBannerForNestedHelp()
    {
        Assert.False(CliProgramBrandPolicy.ShouldPrintBanner(["server", "--help"]));
        Assert.False(CliProgramBrandPolicy.ShouldPrintBanner(["hotfix", "pack", "-h"]));
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
