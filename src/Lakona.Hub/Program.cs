using Avalonia;
namespace Lakona.Hub;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var applicationArgs = HubAotSmokeTest.Capture(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(applicationArgs);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
