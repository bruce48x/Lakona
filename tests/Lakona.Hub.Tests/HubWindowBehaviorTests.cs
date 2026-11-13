using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Lakona.Hub.Applications;
using Lakona.Hub.Sdk;
using Lakona.Hub.Updates;
using Xunit;

namespace Lakona.Hub.Tests;

[CollectionDefinition("Hub headless window", DisableParallelization = true)]
public sealed class HubHeadlessWindowCollection;

[Collection("Hub headless window")]
public sealed class HubWindowBehaviorTests
{
    [Fact]
    public void Window_events_update_frame_state_and_minimize_button_changes_window_state()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lakona-hub-window-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Avalonia is initialized once, on this test thread; the collection cannot run in parallel.
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
            var localization = new HubLocalization(HubLanguage.English);
            var store = new HubUserSettingsStore(Path.Combine(directory, "settings.json"));
            var window = new MainWindow(localization, new HubUpdateService(), new HubSdkManager(),
                new InstalledApplicationCatalog(), new ManualApplicationStore(Path.Combine(directory, "applications.json")),
                store, store.Load(HubLanguage.English), enableStartupDetection: false);
            try
            {
                window.Show();
                var frame = Assert.Single(window.GetLogicalDescendants().OfType<Border>(),
                    border => border.Classes.Contains("window-frame"));
                Dispatcher.UIThread.RunJobs();
                window.Hide();
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("inactive", frame.Classes);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("inactive", frame.Classes);

                window.WindowState = WindowState.Maximized;
                Assert.Contains("maximized", frame.Classes);
                window.WindowState = WindowState.Normal;
                Assert.DoesNotContain("maximized", frame.Classes);

                var minimize = Assert.Single(window.GetLogicalDescendants().OfType<Button>(),
                    button => Equals(ToolTip.GetTip(button), localization.Text.Minimize));
                minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(WindowState.Minimized, window.WindowState);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
