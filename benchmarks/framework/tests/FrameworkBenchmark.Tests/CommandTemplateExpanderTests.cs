using FrameworkBenchmark.Contracts;
using FrameworkBenchmark.Coordinator;
using Xunit;

namespace FrameworkBenchmark.Tests;

public sealed class CommandTemplateExpanderTests
{
    [Fact]
    public void Expand_PreservesArgumentsAndExpandsKnownValues()
    {
        var command = new ProcessCommand(
            "${runtime}",
            ["--path", "${adapterRoot}/with spaces", "--port=${clientPort}"],
            new Dictionary<string, string> { ["CASE"] = "${caseId}" });

        var expanded = CommandTemplateExpander.Expand(
            command,
            new Dictionary<string, string>
            {
                ["runtime"] = "pwsh",
                ["adapterRoot"] = "C:/bench adapter",
                ["clientPort"] = "1234",
                ["caseId"] = "case-1"
            });

        Assert.Equal("pwsh", expanded.FileName);
        Assert.Equal(["--path", "C:/bench adapter/with spaces", "--port=1234"], expanded.Arguments);
        Assert.Equal("case-1", expanded.Environment!["CASE"]);
    }

    [Fact]
    public void Expand_RejectsUnknownPlaceholder()
    {
        var command = new ProcessCommand("pwsh", ["${missing}"]);

        var exception = Assert.Throws<BenchmarkToolException>(() =>
            CommandTemplateExpander.Expand(command, new Dictionary<string, string>()));

        Assert.Contains("${missing}", exception.Message, StringComparison.Ordinal);
    }
}
