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
            "App",
            "Server.App.csproj");
        var artifactsPath = Path.Combine(
            Path.GetTempPath(),
            "LakonaUnityAgarGatewayBuildTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var restore = await RunDotNetAsync(
                repositoryRoot,
                [
                    "restore",
                    projectPath,
                    "--artifacts-path",
                    artifactsPath,
                    "--ignore-failed-sources",
                    "/m:1",
                    "/nr:false",
                    "/p:NuGetAudit=false"
                ]);
            Assert.True(restore.ExitCode == 0, restore.Output);

            var build = await RunDotNetAsync(
                repositoryRoot,
                [
                    "build",
                    projectPath,
                    "--no-restore",
                    "--artifacts-path",
                    artifactsPath,
                    "/m:1",
                    "/nr:false",
                    "/p:UseSharedCompilation=false"
                ]);
            Assert.True(build.ExitCode == 0, build.Output);
        }
        finally
        {
            if (Directory.Exists(artifactsPath))
            {
                Directory.Delete(artifactsPath, recursive: true);
            }
        }
    }

    private static async Task<DotNetResult> RunDotNetAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

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

        return new DotNetResult(
            process.ExitCode,
            output + Environment.NewLine + error);
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

    private sealed record DotNetResult(int ExitCode, string Output);
}
