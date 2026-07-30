using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Lakona.Hub;

internal sealed record StoredHubCrashReport(
    int SchemaVersion,
    string Id,
    DateTimeOffset OccurredAtUtc,
    string Source,
    string Activity,
    string HubVersion,
    string OperatingSystem,
    string Architecture,
    string ExceptionType,
    string Message,
    string StackTrace);

internal sealed record StoredHubSession(int SchemaVersion, DateTimeOffset StartedAtUtc, string HubVersion);

internal static class HubCrashReporter
{
    private const int SchemaVersion = 1;
    private const int MaximumTextLength = 12_000;
    private static readonly object Sync = new();
    private static string activity = "Starting Hub";
    private static string? dataDirectory;
    private static bool handlersRegistered;
    private static bool failureRecordedThisSession;

    public static StoredHubCrashReport? PendingReport { get; private set; }

    public static void Start(string? directory = null, bool registerHandlers = true)
    {
        lock (Sync)
        {
            failureRecordedThisSession = false;
            dataDirectory = directory ?? DefaultDataDirectory();
            Directory.CreateDirectory(dataDirectory);
            PendingReport = LoadReport();
            if (PendingReport is null && File.Exists(SessionPath))
            {
                var session = LoadSession();
                Trace.TraceWarning(
                    session is null
                        ? "The previous Hub session ended without a diagnostic stack trace."
                        : $"The Hub session started at {session.StartedAtUtc:O} ended without a diagnostic stack trace.");
            }

            WriteAtomically(
                SessionPath,
                JsonSerializer.Serialize(
                    new StoredHubSession(SchemaVersion, DateTimeOffset.UtcNow, CurrentVersion),
                    HubJsonContext.Default.StoredHubSession));

            if (registerHandlers && !handlersRegistered)
            {
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
                handlersRegistered = true;
            }
        }
    }

    public static void SetActivity(string value)
    {
        lock (Sync)
        {
            activity = Sanitize(value);
        }
    }

    public static void Record(Exception exception, string source)
    {
        try
        {
            lock (Sync)
            {
                if (failureRecordedThisSession)
                {
                    return;
                }

                if (dataDirectory is null)
                {
                    dataDirectory = DefaultDataDirectory();
                    Directory.CreateDirectory(dataDirectory);
                }

                PendingReport = CreateReport(
                    source,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.Message,
                    exception.StackTrace ?? string.Empty);
                SaveReport(PendingReport);
                failureRecordedThisSession = true;
            }
        }
        catch
        {
            // Crash reporting must never hide or replace the original failure.
        }
    }

    public static void Acknowledge()
    {
        try
        {
            lock (Sync)
            {
                PendingReport = null;
                if (dataDirectory is not null && File.Exists(ReportPath))
                {
                    File.Delete(ReportPath);
                }
            }
        }
        catch
        {
            // A stale report is preferable to failing the application.
        }
    }

    public static void CompleteSession()
    {
        try
        {
            lock (Sync)
            {
                if (dataDirectory is not null && File.Exists(SessionPath))
                {
                    File.Delete(SessionPath);
                }
            }
        }
        catch
        {
            // The next launch can safely treat this as an unexpected termination.
        }
    }

    public static string CreateIssueUrl(StoredHubCrashReport report)
    {
        var title = $"[Hub Crash] {report.ExceptionType}";
        var body = new StringBuilder()
            .AppendLine("## What happened")
            .AppendLine()
            .AppendLine("Lakona Hub detected that the previous session ended unexpectedly.")
            .AppendLine()
            .AppendLine("## Diagnostic report")
            .AppendLine()
            .AppendLine($"- Report ID: `{report.Id}`")
            .AppendLine($"- Time (UTC): `{report.OccurredAtUtc:O}`")
            .AppendLine($"- Hub version: `{report.HubVersion}`")
            .AppendLine($"- OS: `{report.OperatingSystem}`")
            .AppendLine($"- Architecture: `{report.Architecture}`")
            .AppendLine($"- Last action: `{report.Activity}`")
            .AppendLine($"- Source: `{report.Source}`")
            .AppendLine()
            .AppendLine("```text")
            .AppendLine(Truncate($"{report.ExceptionType}: {report.Message}\n{report.StackTrace}", 4_000))
            .AppendLine("```")
            .AppendLine()
            .AppendLine("Paths in this report were automatically redacted. Please review before submitting.")
            .ToString();
        return "https://github.com/bruce48x/Lakona/issues/new?labels=bug%2Chub&title=" +
               Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Record(e.ExceptionObject as Exception ?? new Exception("A non-Exception object was thrown."), "AppDomain");

    private static StoredHubCrashReport CreateReport(string source, string exceptionType, string message, string stackTrace) =>
        new(
            SchemaVersion,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            source,
            activity,
            CurrentVersion,
            Sanitize(RuntimeInformation.OSDescription),
            RuntimeInformation.ProcessArchitecture.ToString(),
            Sanitize(exceptionType),
            Truncate(Sanitize(message), 2_000),
            Truncate(Sanitize(stackTrace), MaximumTextLength));

    private static StoredHubCrashReport? LoadReport()
    {
        try
        {
            var report = File.Exists(ReportPath)
                ? JsonSerializer.Deserialize(File.ReadAllText(ReportPath), HubJsonContext.Default.StoredHubCrashReport)
                : null;
            return report is not null && HasStackTrace(report) ? report : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool HasStackTrace(StoredHubCrashReport report) =>
        !string.IsNullOrWhiteSpace(report.StackTrace);

    private static StoredHubSession? LoadSession()
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(SessionPath), HubJsonContext.Default.StoredHubSession);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void SaveReport(StoredHubCrashReport report) =>
        WriteAtomically(ReportPath, JsonSerializer.Serialize(report, HubJsonContext.Default.StoredHubCrashReport));

    private static void WriteAtomically(string path, string contents)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string Sanitize(string value)
    {
        var result = value;
        foreach (var (path, replacement) in SensitivePaths())
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                result = result.Replace(path, replacement, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private static IEnumerable<(string Path, string Replacement)> SensitivePaths()
    {
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<USER_PROFILE>");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "<LOCAL_APP_DATA>");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "<APP_DATA>");
        yield return (Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "<TEMP>");
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength] + "\n… truncated …";

    private static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    private static string DefaultDataDirectory()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        root = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lakona", "hub")
            : Path.Combine(root, "Lakona", "Hub");
        return Path.Combine(root, "crash-reports");
    }

    private static string ReportPath => Path.Combine(dataDirectory!, "pending-crash.json");
    private static string SessionPath => Path.Combine(dataDirectory!, "active-session.json");
}
