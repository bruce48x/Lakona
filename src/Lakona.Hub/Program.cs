using Avalonia;
using Lakona.Hub.Updates;

namespace Lakona.Hub;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (HubUpdateInstaller.TryRun(args))
        {
            return;
        }

        var applicationArgs = HubUpdateStartup.Capture(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(applicationArgs);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
