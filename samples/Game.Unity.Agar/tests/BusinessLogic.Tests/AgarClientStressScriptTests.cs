using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarClientStressScriptTests
{
    [Fact]
    public void ClientStressScriptBuildsAndStartsAutomatedIsolatedInstances()
    {
        var root = TestHotfix.FindRepositoryRoot();
        var scriptPath = Path.Combine(root, "samples", "Game.Unity.Agar", "client-stress.ps1");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("AgarStressBuild.BuildClient", script, StringComparison.Ordinal);
        Assert.Contains("[CmdletBinding(PositionalBinding = $false)]", script, StringComparison.Ordinal);
        Assert.Contains("[int]$InstanceCount = 10", script, StringComparison.Ordinal);
        Assert.Contains("[Alias(\"Host\")]", script, StringComparison.Ordinal);
        Assert.Contains("[Alias(\"ShowWindows\")]", script, StringComparison.Ordinal);
        Assert.Contains("[int]$StatusIntervalSeconds = 5", script, StringComparison.Ordinal);
        Assert.Contains("[int]$StallTimeoutSeconds = 30", script, StringComparison.Ordinal);
        Assert.Contains("[int]$DurationSeconds = 0", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$Detach", script, StringComparison.Ordinal);
        Assert.Contains("[Alias(\"h\")]", script, StringComparison.Ordinal);
        Assert.Contains("[switch]$Help", script, StringComparison.Ordinal);
        Assert.Contains("ValueFromRemainingArguments = $true", script, StringComparison.Ordinal);
        Assert.Contains("$ExtraArguments -contains \"--help\"", script, StringComparison.Ordinal);
        Assert.Contains("--help, -h, -Help", script, StringComparison.Ordinal);
        Assert.Contains("Server/App/appsettings.json", script, StringComparison.Ordinal);
        Assert.Contains("$defaultEndpoint = @($appSettings.Lakona.Endpoints)", script, StringComparison.Ordinal);
        Assert.Contains("$HostName = [string]$defaultEndpoint.Host", script, StringComparison.Ordinal);
        Assert.Contains("$Port = [int]$defaultEndpoint.Port", script, StringComparison.Ordinal);
        Assert.Contains("if ($IsWindows)", script, StringComparison.Ordinal);
        Assert.Contains("elseif ($IsMacOS)", script, StringComparison.Ordinal);
        Assert.Contains("elseif ($IsLinux)", script, StringComparison.Ordinal);
        Assert.Contains("AgarStressClient.app", script, StringComparison.Ordinal);
        Assert.Contains("Resolve-StressClientExecutable", script, StringComparison.Ordinal);
        Assert.Contains("Contents/MacOS", script, StringComparison.Ordinal);
        Assert.Contains("\"-buildPlatform\", $buildPlatform", script, StringComparison.Ordinal);
        Assert.Contains("\"--stress\"", script, StringComparison.Ordinal);
        Assert.Contains("\"-batchmode\", \"-nographics\"", script, StringComparison.Ordinal);
        Assert.Contains("client-{0:D4}.log", script, StringComparison.Ordinal);
        Assert.Contains("Assert-UnityProjectNotOpen", script, StringComparison.Ordinal);
        Assert.Contains("$buildProcess.WaitForExit()", script, StringComparison.Ordinal);
        Assert.Contains("[System.Diagnostics.Process]::Start($clientStartInfo)", script, StringComparison.Ordinal);
        Assert.Contains("Get-StressLogSnapshot", script, StringComparison.Ordinal);
        Assert.Contains("Show-StressStatus", script, StringComparison.Ordinal);
        Assert.Contains("$state = \"Stalled\"", script, StringComparison.Ordinal);
        Assert.Contains("$client.Round++", script, StringComparison.Ordinal);
        Assert.Contains("LastLeaderboard", script, StringComparison.Ordinal);
        Assert.Contains("Press Ctrl+C to stop this run", script, StringComparison.Ordinal);
        Assert.Contains("Stop-Process -Id $client.Process.Id", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-Help")]
    public void ClientStressScriptPrintsHelpWithoutStartingClients(string helpOption)
    {
        var root = TestHotfix.FindRepositoryRoot();
        var scriptDirectory = Path.Combine(root, "samples", "Game.Unity.Agar");
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = scriptDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add($".\\client-stress.ps1 {helpOption}");

        using var process = System.Diagnostics.Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("USAGE", standardOutput, StringComparison.Ordinal);
        Assert.Contains("-InstanceCount", standardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Building", standardOutput, StringComparison.Ordinal);
        Assert.True(string.IsNullOrWhiteSpace(standardError), standardError);
    }

    [Fact]
    public void UnityClientSupportsStressBuildAndAutomatedMatchmaking()
    {
        var root = TestHotfix.FindRepositoryRoot();
        var client = Path.Combine(root, "samples", "Game.Unity.Agar", "Client", "Assets");
        var build = File.ReadAllText(Path.Combine(client, "Editor", "AgarStressBuild.cs"));
        var launchArguments = File.ReadAllText(Path.Combine(client, "Scripts", "Rpc", "RpcLaunchArguments.cs"));
        var game = File.ReadAllText(Path.Combine(client, "Scripts", "Gameplay", "DotArenaGame.cs"));
        var session = File.ReadAllText(Path.Combine(client, "Scripts", "Gameplay", "DotArenaGame.Session.cs"));
        var callbacks = File.ReadAllText(Path.Combine(client, "Scripts", "Gameplay", "DotArenaGame.Callbacks.cs"));
        var meta = File.ReadAllText(Path.Combine(client, "Scripts", "Gameplay", "DotArenaGame.Meta.cs"));

        Assert.Contains("BuildTarget.StandaloneWindows64", build, StringComparison.Ordinal);
        Assert.Contains("BuildTarget.StandaloneOSX", build, StringComparison.Ordinal);
        Assert.Contains("BuildTarget.StandaloneLinux64", build, StringComparison.Ordinal);
        Assert.Contains("public bool StressMode", launchArguments, StringComparison.Ordinal);
        Assert.Contains("key == \"stress\"", launchArguments, StringComparison.Ordinal);
        Assert.Contains("StartStressClientAsync", game, StringComparison.Ordinal);
        Assert.Contains("await ConnectAsGuestAsync()", session, StringComparison.Ordinal);
        Assert.Contains("BeginMultiplayerMatchmaking();", session, StringComparison.Ordinal);
        Assert.Contains("[Stress] Matchmaking requested", session, StringComparison.Ordinal);
        Assert.Contains("[Stress] Settlement submitted", callbacks, StringComparison.Ordinal);
        Assert.Contains("[Stress] Leaderboard refreshed", meta, StringComparison.Ordinal);
        Assert.Contains("_stressMode && _flowState == FrontendFlowState.Settlement", game, StringComparison.Ordinal);
    }
}
