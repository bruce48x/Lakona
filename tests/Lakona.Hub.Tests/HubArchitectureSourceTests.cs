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
