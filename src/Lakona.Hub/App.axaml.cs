using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Lakona.Hub;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow(enableStartupDetection: !HubAotSmokeTest.IsRequested);
            desktop.MainWindow = mainWindow;
            if (HubAotSmokeTest.IsRequested)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.UIThread.Post(async () =>
                {
                    var exitCode = await HubAotSmokeTest.RunAsync();
                    desktop.Shutdown(exitCode);
                });
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
