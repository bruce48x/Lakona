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
