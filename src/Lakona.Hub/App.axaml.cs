using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Lakona.Hub.Updates;

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
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            mainWindow.ShowUpdateFailure(HubUpdateStartup.TakeFailureMessage());
            HubUpdateStartup.Complete();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
