using System.Text.Json;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubCrashReporterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"lakona-hub-crash-tests-{Guid.NewGuid():N}");

    [Fact]
    public void Record_PersistsRedactedDiagnosticsAndCreatesPrefilledIssue()
    {
        HubCrashReporter.Start(root, registerHandlers: false);
        HubCrashReporter.SetActivity($"Opening {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)} project");

        HubCrashReporter.Record(
            new InvalidOperationException($"Failed under {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}"),
            "Test");

        var report = Assert.IsType<StoredHubCrashReport>(HubCrashReporter.PendingReport);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), report.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<USER_PROFILE>", report.Message, StringComparison.Ordinal);
        Assert.Contains("github.com/bruce48x/Lakona/issues/new", HubCrashReporter.CreateIssueUrl(report), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "pending-crash.json")));
    }

    [Fact]
    public void Start_ConvertsAnUnfinishedSessionIntoAPendingReport()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "active-session.json"),
            JsonSerializer.Serialize(
                new StoredHubSession(1, DateTimeOffset.UtcNow.AddMinutes(-2), "0.3.2"),
                HubJsonContext.Default.StoredHubSession));

        HubCrashReporter.Start(root, registerHandlers: false);

        Assert.Equal("UnexpectedTermination", HubCrashReporter.PendingReport?.Source);
    }

    public void Dispose()
    {
        HubCrashReporter.CompleteSession();
        HubCrashReporter.Acknowledge();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
