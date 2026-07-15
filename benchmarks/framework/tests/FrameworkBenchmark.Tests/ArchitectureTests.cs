using Xunit;

namespace FrameworkBenchmark.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void NeutralProjectsDoNotReferenceFrameworkAdaptersOrLakonaRuntime()
    {
        var projectFiles = new[]
        {
            Path.Combine(TestPaths.BenchmarkRoot, "src", "FrameworkBenchmark.Contracts", "FrameworkBenchmark.Contracts.csproj"),
            Path.Combine(TestPaths.BenchmarkRoot, "src", "FrameworkBenchmark.Coordinator", "FrameworkBenchmark.Coordinator.csproj")
        };

        foreach (var projectFile in projectFiles)
        {
            var text = File.ReadAllText(projectFile);
            Assert.DoesNotContain("src/Lakona.", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("adapters/", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
