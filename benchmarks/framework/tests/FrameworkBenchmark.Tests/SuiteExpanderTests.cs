using FrameworkBenchmark.Contracts;
using FrameworkBenchmark.Coordinator;
using Xunit;

namespace FrameworkBenchmark.Tests;

public sealed class SuiteExpanderTests
{
    [Fact]
    public void Expand_V1SuiteProducesStableFortyEightCaseMatrix()
    {
        var suite = BenchmarkJson.Read<BenchmarkSuite>(Path.Combine(TestPaths.BenchmarkRoot, "suites", "v1.json"));

        var first = SuiteExpander.Expand(suite);
        var second = SuiteExpander.Expand(suite);

        Assert.Equal(48, first.Count);
        Assert.Equal(first.Select(static item => item.Id), second.Select(static item => item.Id));
        Assert.Equal(first.Count, first.Select(static item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("framework-v1-lakona-cluster-direct-p32-c1", first[0].Id);
    }

    [Fact]
    public void Expand_RejectsUnknownWorkload()
    {
        var suite = CreateSuite() with { Workloads = ["unknown"] };

        var exception = Assert.Throws<InvalidDataException>(() => SuiteExpander.Expand(suite));

        Assert.Contains("unknown workload", exception.Message, StringComparison.Ordinal);
    }

    internal static BenchmarkSuite CreateSuite(string framework = "fake")
    {
        return new BenchmarkSuite(
            BenchmarkSchemaVersions.V1,
            "fixture",
            [framework],
            ["frontdoor.echo"],
            [32],
            [1],
            7,
            new SuiteTiming(5000, 3000, 0, 1, 100, 100, 1000),
            new HistogramConfiguration("microseconds", 1, 60000000, 3));
    }
}
