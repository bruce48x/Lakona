using System.Diagnostics;
using Xunit;

namespace Lakona.Tool.Tests.Samples;

public sealed class UnityAgarGatewayBuildTests
{
    [Fact]
    public async Task UnityAgarGatewayProjectBuilds()
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "samples",
            "Game.Unity.Agar",
            "Server",
            "Gateway",
            "Gateway.csproj");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("/m:1");
        startInfo.ArgumentList.Add("/nr:false");
        startInfo.ArgumentList.Add("/p:UseSharedCompilation=false");
        using var process = Process.Start(startInfo);

        Assert.NotNull(process);

        var outputTask = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var exitTask = process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(180), TestContext.Current.CancellationToken));
        if (completed != exitTask)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Unity Agar gateway build did not finish within 180 seconds.");
        }

        await exitTask;
        var output = await outputTask;
        var error = await errorTask;

        Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
