using System.Globalization;
using System.Text;

namespace Lakona.Game.LoadTesting;

public static class LoadRunSummaryFormatter
{
    public static string Format(LoadRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Scenario: {summary.ScenarioName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Users: {summary.ConfiguredUsers} configured, {summary.StartedUsers} started, {summary.CompletedUsers} completed");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Operations: {summary.TotalOperations} total, {summary.SucceededOperations} succeeded, {summary.FailedOperations} failed, {summary.CanceledOperations} canceled");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Elapsed: {summary.Elapsed:c}");

        if (summary.Latencies.Count > 0)
        {
            builder.AppendLine("Latencies:");
            foreach (var latency in summary.Latencies)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  {latency.OperationName}: count={latency.Count}, avg={FormatMilliseconds(latency.Average)}, p50={FormatMilliseconds(latency.P50)}, p95={FormatMilliseconds(latency.P95)}, p99={FormatMilliseconds(latency.P99)}");
            }
        }

        if (summary.Errors.Count > 0)
        {
            builder.AppendLine("Errors:");
            foreach (var error in summary.Errors)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"  {error.OperationName}: {error.ExceptionType}: {error.Message} ({error.Count})");
            }
        }

        return builder.ToString();
    }

    private static string FormatMilliseconds(TimeSpan value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{value.TotalMilliseconds:0.##} ms");
    }
}
