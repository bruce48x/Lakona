using System.Globalization;
using System.Text;
using FrameworkBenchmark.Contracts;

namespace FrameworkBenchmark.Coordinator;

internal static class BenchmarkReportWriter
{
    public static void Write(string path, RunSummary summary)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Local Framework Benchmark");
        builder.AppendLine();
        builder.AppendLine("> Development evidence only. The load driver and server processes shared one workstation; this is not a publishable network-cluster comparison.");
        builder.AppendLine();
        builder.AppendLine($"- Run: `{summary.RunId}`");
        builder.AppendLine($"- Suite: `{summary.SuiteId}`");
        builder.AppendLine($"- Mode: `{summary.Mode}`");
        builder.AppendLine();
        builder.AppendLine("Ratios compare each cluster path with the matching framework, payload, and concurrency `frontdoor.echo` baseline; latencies are not subtracted.");
        builder.AppendLine();
        builder.AppendLine("| Workload | Payload | Concurrency | Framework | Valid | RPS | RPS / echo | p50 us | p50 / echo | p95 us | p99 us | Max us | Errors |");
        builder.AppendLine("| --- | ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");

        var baselines = summary.Cases
            .Where(static item => item.Case.Workload == "frontdoor.echo")
            .ToDictionary(
                static item => (item.Case.Framework, item.Case.PayloadSize, item.Case.Concurrency));

        foreach (var item in summary.Cases.OrderBy(static item => item.Case.Workload, StringComparer.Ordinal)
                     .ThenBy(static item => item.Case.PayloadSize)
                     .ThenBy(static item => item.Case.Concurrency)
                     .ThenBy(static item => item.Case.Framework, StringComparer.Ordinal))
        {
            baselines.TryGetValue(
                (item.Case.Framework, item.Case.PayloadSize, item.Case.Concurrency),
                out var baseline);
            var p50 = Percentile(item.Result.Histogram, 0.50);
            var baselineP50 = baseline is null ? 0 : Percentile(baseline.Result.Histogram, 0.50);
            builder.Append("| ").Append(item.Case.Workload)
                .Append(" | ").Append(item.Case.PayloadSize.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(item.Case.Concurrency.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(item.Case.Framework)
                .Append(" | ").Append(item.IsValid ? "yes" : "no")
                .Append(" | ").Append(item.Result.AchievedRequestsPerSecond.ToString("F2", CultureInfo.InvariantCulture))
                .Append(" | ").Append(Ratio(item.Result.AchievedRequestsPerSecond, baseline?.Result.AchievedRequestsPerSecond ?? 0))
                .Append(" | ").Append(p50.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(Ratio(p50, baselineP50))
                .Append(" | ").Append(Percentile(item.Result.Histogram, 0.95).ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(Percentile(item.Result.Histogram, 0.99).ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(item.Result.Histogram.Maximum.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(item.Errors.Count == 0 ? "-" : string.Join("; ", item.Errors))
                .AppendLine(" |");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, builder.ToString());
    }

    private static string Ratio(double value, double baseline) => baseline <= 0
        ? "-"
        : $"{(value / baseline).ToString("F2", CultureInfo.InvariantCulture)}x";

    internal static long Percentile(LatencyHistogram histogram, double percentile)
    {
        if (histogram.TotalCount == 0)
        {
            return 0;
        }

        var target = (long)Math.Ceiling(histogram.TotalCount * percentile);
        long seen = 0;
        foreach (var bucket in histogram.Buckets)
        {
            seen += bucket.Count;
            if (seen >= target)
            {
                return bucket.UpperBound;
            }
        }

        return histogram.Maximum;
    }
}
