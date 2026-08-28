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
