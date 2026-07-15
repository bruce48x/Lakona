using System.Globalization;
using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

public static class SuiteExpander
{
    public static IReadOnlyList<BenchmarkCase> Expand(BenchmarkSuite suite)
    {
        BenchmarkDefinitionValidator.Validate(suite);
        var cases = new List<BenchmarkCase>();

        foreach (var workload in suite.Workloads.Order(StringComparer.Ordinal))
        {
            foreach (var payloadSize in suite.PayloadSizes.Order())
            {
                foreach (var concurrency in suite.Concurrency.Order())
                {
                    foreach (var framework in suite.Frameworks.Order(StringComparer.Ordinal))
                    {
                        var id = string.Join(
                            '-',
                            suite.Id,
                            framework,
                            workload.Replace('.', '-'),
                            $"p{payloadSize.ToString(CultureInfo.InvariantCulture)}",
                            $"c{concurrency.ToString(CultureInfo.InvariantCulture)}");
                        cases.Add(new BenchmarkCase(
                            id,
                            suite.Id,
                            framework,
                            workload,
                            payloadSize,
                            concurrency,
                            suite.Seed,
                            suite.Timing,
                            suite.Histogram));
                    }
                }
            }
        }

        return cases;
    }
}
