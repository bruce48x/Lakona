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

        Assert.Contains("AgarStressBuild.BuildWindowsClient", script, StringComparison.Ordinal);
        Assert.Contains("[int]$InstanceCount = 10", script, StringComparison.Ordinal);
        Assert.Contains("[Alias(\"Host\")]", script, StringComparison.Ordinal);
        Assert.Contains("Server\\App\\appsettings.json", script, StringComparison.Ordinal);
        Assert.Contains("$defaultEndpoint = @($appSettings.Lakona.Endpoints)", script, StringComparison.Ordinal);
        Assert.Contains("$HostName = [string]$defaultEndpoint.Host", script, StringComparison.Ordinal);
        Assert.Contains("$Port = [int]$defaultEndpoint.Port", script, StringComparison.Ordinal);
        Assert.Contains("\"--stress\"", script, StringComparison.Ordinal);
        Assert.Contains("\"-batchmode\", \"-nographics\"", script, StringComparison.Ordinal);
        Assert.Contains("client-{0:D4}.log", script, StringComparison.Ordinal);
        Assert.Contains("Assert-UnityProjectNotOpen", script, StringComparison.Ordinal);
        Assert.Contains("$buildProcess.WaitForExit()", script, StringComparison.Ordinal);
        Assert.Contains("[System.Diagnostics.Process]::Start($clientStartInfo)", script, StringComparison.Ordinal);
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

        Assert.Contains("BuildTarget.StandaloneWindows64", build, StringComparison.Ordinal);
        Assert.Contains("public bool StressMode", launchArguments, StringComparison.Ordinal);
        Assert.Contains("key == \"stress\"", launchArguments, StringComparison.Ordinal);
        Assert.Contains("StartStressClientAsync", game, StringComparison.Ordinal);
        Assert.Contains("await ConnectAsGuestAsync()", session, StringComparison.Ordinal);
        Assert.Contains("BeginMultiplayerMatchmaking();", session, StringComparison.Ordinal);
        Assert.Contains("_stressMode && _flowState == FrontendFlowState.Settlement", game, StringComparison.Ordinal);
    }
}
