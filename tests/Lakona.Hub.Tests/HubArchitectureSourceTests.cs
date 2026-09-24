using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubArchitectureSourceTests
{
    [Fact]
    public void MainWindowComposesWorkflowsWithoutOwningTheirServiceStateMachines()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "MainWindow.axaml.cs"));

        Assert.Contains("new HubEnvironmentWorkflow(", source, StringComparison.Ordinal);
        Assert.Contains("new HubUpdateWorkflow(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("applicationRegistry.DetectAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sdkManager.InspectAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sdkManager.InstallAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("updateService.CheckAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("updateService.PrepareAndLaunchAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Packaging_dialog_keeps_actions_outside_its_bounded_scrollable_content()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "MainWindow.axaml"));

        Assert.Contains("x:Name=\"PackageDialogSurface\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"720\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PackageDialogScrollViewer\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PackageDialogActions\"", source, StringComparison.Ordinal);
        Assert.True(
            source.IndexOf("x:Name=\"PackageDialogScrollViewer\"", StringComparison.Ordinal)
            < source.IndexOf("x:Name=\"PackageDialogActions\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Project_creation_exposes_a_bounded_progress_dialog()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "MainWindow.axaml"));

        Assert.Contains("IsVisible=\"{Binding CreationForm.IsCreating}\"", source, StringComparison.Ordinal);
        Assert.Contains("Value=\"{Binding CreationProgressValue}\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CreationProgressText}\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_frame_distinguishes_activation_and_maximized_states()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "MainWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "MainWindow.axaml.cs"));

        Assert.Contains("Border.window-frame.inactive", xaml, StringComparison.Ordinal);
        Assert.Contains("HubBrush.WindowBorderActive", xaml, StringComparison.Ordinal);
        Assert.Contains("HubBrush.WindowBorderInactive", xaml, StringComparison.Ordinal);
        Assert.Contains("HubShadow.WindowActive", xaml, StringComparison.Ordinal);
        Assert.Contains("HubShadow.WindowInactive", xaml, StringComparison.Ordinal);
        Assert.Contains("Border.window-frame.maximized", xaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"BorderThickness\" Value=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Activated += MainWindow_FrameActivated", code, StringComparison.Ordinal);
        Assert.Contains("Deactivated += MainWindow_FrameDeactivated", code, StringComparison.Ordinal);
        Assert.Contains("WindowFrame.Classes.Set(\"inactive\", true)", code, StringComparison.Ordinal);
        Assert.Contains("WindowFrame.Classes.Set(\"inactive\", false)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_frame_keeps_rounded_outline_and_uses_direct_minimize()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "MainWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "MainWindow.axaml.cs"));

        Assert.Contains("ExtendClientAreaToDecorationsHint=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowDecorations=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WindowSurface\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"window-outline\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Border.window-outline", xaml, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", xaml, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("x:Name=\"WindowSurface\"", StringComparison.Ordinal)
            < xaml.IndexOf("Classes=\"window-outline\"", StringComparison.Ordinal));
        Assert.DoesNotContain("WindowDecorationProperties.ElementRole=\"MinimizeButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("private void Minimize_Click", code, StringComparison.Ordinal);
        Assert.Contains("=> WindowState = WindowState.Minimized;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Appearance_uses_shared_theme_dictionaries_and_exposes_all_three_choices()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "App.axaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "MainWindow.axaml"));
        var settings = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "HubThemeSettings.cs"));

        Assert.Contains("RequestedThemeVariant=\"Default\"", app, StringComparison.Ordinal);
        Assert.Contains("<ResourceDictionary x:Key=\"Dark\">", app, StringComparison.Ordinal);
        Assert.Contains("<ResourceDictionary x:Key=\"Light\">", app, StringComparison.Ordinal);
        Assert.Equal(
            2,
            app.Split("<SolidColorBrush x:Key=\"HubBrush.Accent\" Color=\"#EFBE3E\" />", StringSplitOptions.None).Length - 1);
        Assert.Contains("<SolidColorBrush x:Key=\"HubBrush.Window\" Color=\"#F2EFE8\" />", app, StringComparison.Ordinal);
        Assert.Contains("<GradientStop Color=\"#EDE8DE\" Offset=\"0\" />", app, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ThemeSettingsCard\"", window, StringComparison.Ordinal);
        Assert.Contains("HubThemePreference.System", settings, StringComparison.Ordinal);
        Assert.Contains("HubThemePreference.Dark", settings, StringComparison.Ordinal);
        Assert.Contains("HubThemePreference.Light", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("OperatingSystem.Is", settings, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the Lakona repository root.");
    }
}
