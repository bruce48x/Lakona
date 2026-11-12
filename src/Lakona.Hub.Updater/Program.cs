using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Lakona.Hub.Installation;

namespace Lakona.Hub.Updater;

internal static class Program
{
    internal static string[] Arguments = [];
    [STAThread]
    public static void Main(string[] args)
    {
        Arguments = args;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This installer supports Windows only.");
        using var workLease = InstallationPaths.Current.HoldWork(AppContext.BaseDirectory);
        if (InstallationPaths.Current.Owns(AppContext.BaseDirectory) && (args.Contains("--uninstall") || args.Contains("--recover")))
        {
            // Run outside the application directory so Windows can replace/remove
            // the updater and its native graphics libraries without file locks.
            var paths = InstallationPaths.Current;
            paths.Prepare();
            var destination = Path.Combine(paths.State, "handoff-" + Guid.NewGuid().ToString("N"));
            SignedPackage.Release().CopyUpdater(paths.Application, destination);
            var start = new ProcessStartInfo(Path.Combine(destination, "Lakona.Hub.Updater.exe")) { UseShellExecute = false, WorkingDirectory = destination };
            foreach (var argument in args) start.ArgumentList.Add(argument);
            Process.Start(start)?.Dispose();
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<InstallerApplication>().UsePlatformDetect();
    internal static string? Option(string name)
    {
        var index = Array.IndexOf(Arguments, name);
        return index >= 0 && index + 1 < Arguments.Length ? Arguments[index + 1] : null;
    }
}

public sealed partial class InstallerApplication : Application
{
    public override void Initialize()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        RequestedThemeVariant = Program.Option("--theme") switch { "Dark" => ThemeVariant.Dark, "Light" => ThemeVariant.Light, _ => ThemeVariant.Default };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new InstallerWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
