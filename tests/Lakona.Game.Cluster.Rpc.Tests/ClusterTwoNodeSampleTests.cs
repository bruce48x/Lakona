using System.Diagnostics;
using Xunit;

namespace Lakona.Game.Cluster.Rpc.Tests;

public sealed class ClusterTwoNodeSampleTests
{
    [Fact]
    public async Task DriverRunsDirectoryAndWorkerAsSeparateProcesses()
    {
        var repoRoot = FindRepoRoot();
        var sampleProject = Path.Combine(repoRoot, "samples", "Cluster.TwoNode", "Cluster.TwoNode.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(sampleProject);
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--mode");
        startInfo.ArgumentList.Add("driver");

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start Cluster.TwoNode sample.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.True(process.ExitCode == 0, $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
        Assert.Contains("node-directory-ready", stdout, StringComparison.Ordinal);
        Assert.Contains("node-registered node=worker epoch=1", stdout, StringComparison.Ordinal);
        Assert.Contains("node-restarted node=worker epoch=2", stdout, StringComparison.Ordinal);
        Assert.Contains("remote=Accepted", stdout, StringComparison.Ordinal);
        Assert.Contains("missing=RouteNotFound", stdout, StringComparison.Ordinal);
        Assert.Contains("expired=Expired", stdout, StringComparison.Ordinal);
        Assert.Contains("timeout=Timeout", stdout, StringComparison.Ordinal);
        Assert.Contains("backpressure=Backpressure", stdout, StringComparison.Ordinal);
        Assert.Contains("handlerUnavailable=HandlerUnavailable", stdout, StringComparison.Ordinal);
        Assert.Contains("staleRegister=StaleLocation", stdout, StringComparison.Ordinal);
        Assert.Contains("oldRouteAfterClear=null", stdout, StringComparison.Ordinal);
        Assert.Contains("afterRestart=Accepted", stdout, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CONTRIBUTING.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "samples", "Cluster.TwoNode")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
