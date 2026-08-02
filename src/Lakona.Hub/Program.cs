using Avalonia;
using Lakona.Hub.Updates;
namespace Lakona.Hub;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (WindowsUpdateWorker.TryParse(args, out var updateWorkerRequest))
        {
            Environment.ExitCode = WindowsUpdateWorker.RunAsync(
                    updateWorkerRequest!,
                    new SystemHubUpdateProcessFactory(),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return;
        }

        var applicationArgs = HubAotSmokeTest.Capture(args);
        try
        {
            HubCrashReporter.Start(registerHandlers: !HubAotSmokeTest.IsRequested);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceError($"Lakona Hub crash reporting could not start: {ex.Message}");
        }
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(applicationArgs);
        }
        finally
        {
            HubCrashReporter.CompleteSession();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
