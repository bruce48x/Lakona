using System.Diagnostics;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarServerControlScriptTests
{
    [Fact]
    public void ServerControlScriptSupportsLifecycleCommandsAndReadiness()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "server-ctl.ps1");

        Assert.True(File.Exists(scriptPath), "The Agar server control script should exist.");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("#Requires -Version 7.0", script, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet(\"start\", \"status\", \"stop\", \"logs\", \"help\")]", script, StringComparison.Ordinal);
        Assert.Contains("docker compose", script, StringComparison.Ordinal);
        Assert.Contains("\"up\", \"--detach\"", script, StringComparison.Ordinal);
        Assert.Contains("\"down\"", script, StringComparison.Ordinal);
        Assert.Contains("\"logs\", \"--tail\"", script, StringComparison.Ordinal);
        Assert.Contains("/_lakona/health/ready", script, StringComparison.Ordinal);
        Assert.Contains("Wait-ForClusterReady", script, StringComparison.Ordinal);
        Assert.Contains("AGAR_GATEWAY_MANAGEMENT_PORT", script, StringComparison.Ordinal);
        Assert.Contains("AGAR_DATA_MANAGEMENT_PORT", script, StringComparison.Ordinal);
        Assert.Contains("AGAR_BATTLE_MANAGEMENT_PORT", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HelpCommandRunsWithoutDocker()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "server-ctl.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("help");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pwsh.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("Game.Unity.Agar server control", output, StringComparison.Ordinal);
        Assert.Contains("start", output, StringComparison.Ordinal);
        Assert.Contains("status", output, StringComparison.Ordinal);
        Assert.Contains("stop", output, StringComparison.Ordinal);
        Assert.Contains("logs", output, StringComparison.Ordinal);
        Assert.Contains("help", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SingleNodeUnityMcpScriptFallsBackToManagedSingleTopology()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "game",
            "local",
            "test-agar-single-node-unity-mcp.ps1");

        Assert.True(File.Exists(scriptPath), "The single-node Unity MCP script should exist.");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("server-ctl.ps1", script, StringComparison.Ordinal);
        Assert.Contains("start -Topology single", script, StringComparison.Ordinal);
        Assert.Contains("server-ctl.started", script, StringComparison.Ordinal);
        Assert.Contains("Stop-ManagedCompose", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples", "Game.Unity.Agar")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from '{AppContext.BaseDirectory}'.");
    }
}
