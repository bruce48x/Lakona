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

        var exception = Assert.Throws<InvalidOperationException>(ThrowRecordedCrash);
        HubCrashReporter.Record(exception, "Test");

        var report = Assert.IsType<StoredHubCrashReport>(HubCrashReporter.PendingReport);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), report.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<USER_PROFILE>", report.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ThrowRecordedCrash), report.StackTrace, StringComparison.Ordinal);
        Assert.Contains("github.com/bruce48x/Lakona/issues/new", HubCrashReporter.CreateIssueUrl(report), StringComparison.Ordinal);
        var storedReport = JsonSerializer.Deserialize(
            File.ReadAllText(Path.Combine(root, "pending-crash.json")),
            HubJsonContext.Default.StoredHubCrashReport);
        Assert.Contains(nameof(ThrowRecordedCrash), storedReport!.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_DoesNotExposeAnUnfinishedSessionWithoutAStackTrace()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "active-session.json"),
            JsonSerializer.Serialize(
                new StoredHubSession(1, DateTimeOffset.UtcNow.AddMinutes(-2), "0.3.2"),
                HubJsonContext.Default.StoredHubSession));

        HubCrashReporter.Start(root, registerHandlers: false);

        Assert.Null(HubCrashReporter.PendingReport);
    }

    [Fact]
    public void Start_DoesNotExposeAStoredReportWithoutAStackTrace()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, "pending-crash.json"),
            JsonSerializer.Serialize(
                new StoredHubCrashReport(
                    1,
                    "report-without-stack",
                    DateTimeOffset.UtcNow,
                    "Test",
                    "Starting Hub",
                    "0.5.63",
                    "Windows",
                    "X64",
                    typeof(InvalidOperationException).FullName!,
                    "Crash without diagnostic frames",
                    ""),
                HubJsonContext.Default.StoredHubCrashReport));

        HubCrashReporter.Start(root, registerHandlers: false);

        Assert.Null(HubCrashReporter.PendingReport);
    }

    [Fact]
    public void Start_DoesNotExposeARecordedExceptionThatHasNoStackTrace()
    {
        HubCrashReporter.Start(root, registerHandlers: false);
        HubCrashReporter.Record(new InvalidOperationException("Never thrown"), "Test");
        HubCrashReporter.CompleteSession();

        HubCrashReporter.Start(root, registerHandlers: false);

        Assert.Null(HubCrashReporter.PendingReport);
    }

    private static void ThrowRecordedCrash() =>
        throw new InvalidOperationException(
            $"Failed under {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");

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
