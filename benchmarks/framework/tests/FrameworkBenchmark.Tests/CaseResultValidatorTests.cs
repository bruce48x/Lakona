using FrameworkBenchmark.Contracts;
using FrameworkBenchmark.Coordinator;
using Xunit;

namespace FrameworkBenchmark.Tests;

public sealed class CaseResultValidatorTests
{
    [Fact]
    public void Validate_AcceptsCompleteCorrectResult()
    {
        var benchmarkCase = SuiteExpander.Expand(SuiteExpanderTests.CreateSuite()).Single();

        var validated = CaseResultValidator.Validate(benchmarkCase, CreateResult(benchmarkCase));

        Assert.True(validated.IsValid);
        Assert.Empty(validated.Errors);
    }

    [Fact]
    public void Validate_RejectsCorruptionAndCountMismatch()
    {
        var benchmarkCase = SuiteExpander.Expand(SuiteExpanderTests.CreateSuite()).Single();
        var result = CreateResult(benchmarkCase) with
        {
            Outcomes = new CaseOutcomeCounts(4, 4, 3, 0, 1, 0, 0, 0, 0, 0),
            Histogram = CreateResult(benchmarkCase).Histogram with { TotalCount = 3 }
        };

        var validated = CaseResultValidator.Validate(benchmarkCase, result);

        Assert.False(validated.IsValid);
        Assert.Contains(validated.Errors, static error => error.Contains("correctness threshold", StringComparison.Ordinal));
        Assert.Contains(validated.Errors, static error => error.Contains("histogram totalCount", StringComparison.Ordinal));
    }

    internal static CaseResult CreateResult(BenchmarkCase benchmarkCase)
    {
        return new CaseResult(
            BenchmarkSchemaVersions.V1,
            benchmarkCase.Id,
            benchmarkCase.Framework,
            benchmarkCase.Workload,
            4000,
            new CaseOutcomeCounts(4, 4, 4, 0, 0, 0, 0, 0, 0, 0),
            new LatencyHistogram("microseconds", 1, 60000000, 3, 4, 100, [new HistogramBucket(100, 4)]),
            new Dictionary<string, string> { ["runtime"] = "fixture" });
    }
}
