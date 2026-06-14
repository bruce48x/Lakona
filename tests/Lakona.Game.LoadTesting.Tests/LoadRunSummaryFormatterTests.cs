using Lakona.Game.LoadTesting;
using Xunit;

namespace Lakona.Game.LoadTesting.Tests;

public sealed class LoadRunSummaryFormatterTests
{
    [Fact]
    public void Format_IncludesUsersOperationsFailuresElapsedAndP95()
    {
        var summary = new LoadRunSummary(
            "chat",
            ConfiguredUsers: 2,
            StartedUsers: 2,
            CompletedUsers: 2,
            TotalOperations: 4,
            SucceededOperations: 3,
            FailedOperations: 1,
            CanceledOperations: 0,
            FailedUsers: 1,
            Elapsed: TimeSpan.FromSeconds(3),
            Latencies:
            [
                new LoadOperationLatencySummary("login", 2, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(9), TimeSpan.FromMilliseconds(11), TimeSpan.FromMilliseconds(12))
            ],
            Errors:
            [
                new LoadErrorSummary("login", nameof(InvalidOperationException), "login rejected", 1)
            ]);

        var text = LoadRunSummaryFormatter.Format(summary);

        Assert.Contains("Scenario: chat", text, StringComparison.Ordinal);
        Assert.Contains("Users: 2 configured, 2 started, 2 completed", text, StringComparison.Ordinal);
        Assert.Contains("Operations: 4 total, 3 succeeded, 1 failed, 0 canceled", text, StringComparison.Ordinal);
        Assert.Contains("User failures: 1", text, StringComparison.Ordinal);
        Assert.Contains("Elapsed: 00:00:03", text, StringComparison.Ordinal);
        Assert.Contains("login", text, StringComparison.Ordinal);
        Assert.Contains("p95=11 ms", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
    }
}
