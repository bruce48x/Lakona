using System.Text.RegularExpressions;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarThreeNodeLocalTestScriptTests
{
    [Fact]
    public void ThreeNodeLocalScriptUsesUnityPlayModeAndComposeOverrides()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "game",
            "ci",
            "test-agar-three-node.ps1");

        Assert.True(File.Exists(scriptPath), "The local Agar three-node script should exist.");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("#Requires -Version 7.0", script, StringComparison.Ordinal);
        Assert.Contains("docker compose", script, StringComparison.Ordinal);
        Assert.Contains("container_name: lakona-agar-three-node-test-gateway-1", script, StringComparison.Ordinal);
        Assert.Contains("container_name: lakona-agar-three-node-test-battle-1", script, StringComparison.Ordinal);
        Assert.Contains("subnet: 10.10.0.0/24", script, StringComparison.Ordinal);
        Assert.Contains("gateway: 10.10.0.254", script, StringComparison.Ordinal);
        Assert.Contains("ip_range: 10.10.0.128/25", script, StringComparison.Ordinal);
        Assert.Contains("Test-TcpPortFree", script, StringComparison.Ordinal);
        Assert.Contains("Test-UdpPortFree", script, StringComparison.Ordinal);
        Assert.Contains("Test-DockerPublishedPortFree", script, StringComparison.Ordinal);
        Assert.Contains("Port 20000/tcp is already in use", script, StringComparison.Ordinal);
        Assert.Contains("Port 20001/udp is already in use", script, StringComparison.Ordinal);
        Assert.Contains("ipv4_address: 10.10.0.2", script, StringComparison.Ordinal);
        Assert.Contains("ipv4_address: 10.10.0.3", script, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.10.0.2:21002", script, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.10.0.3:21003", script, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints: >-", script, StringComparison.Ordinal);
        Assert.Contains("\"AdvertisedHost\": \"127.0.0.1\"", script, StringComparison.Ordinal);
        Assert.Contains("ports: !reset []", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Endpoints__0__AdvertisedHost", script, StringComparison.Ordinal);
        Assert.Contains("gateway-1", script, StringComparison.Ordinal);
        Assert.Contains("battle-1", script, StringComparison.Ordinal);
        Assert.Contains("-runTests", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("(?m)^\\s*\"-quit\",?\\s*$", RegexOptions.CultureInvariant), script);
        Assert.Contains("-testPlatform", script, StringComparison.Ordinal);
        Assert.Contains("PlayMode", script, StringComparison.Ordinal);
        Assert.Contains("TestResults.xml", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $testResults", script, StringComparison.Ordinal);
        Assert.Contains("Assert-UnityTestResults", script, StringComparison.Ordinal);
        Assert.Contains("[xml]", script, StringComparison.Ordinal);
        Assert.Contains("SampleClient.Gameplay.Tests.DotArenaThreeNodePlayModeTests.UnityClientCompletesThreeNodeMultiplayerSmoke", script, StringComparison.Ordinal);
        Assert.Contains("Test result XML was not created", script, StringComparison.Ordinal);
        Assert.Contains("SelectSingleNode(\"/test-run\")", script, StringComparison.Ordinal);
        Assert.Contains("foreach ($countName in @(\"failed\", \"skipped\", \"inconclusive\"))", script, StringComparison.Ordinal);
        Assert.Contains("Unity test run result was", script, StringComparison.Ordinal);
        Assert.Contains("Unity target test '$TargetTest' result was", script, StringComparison.Ordinal);
        Assert.Contains("unity-editor.log", script, StringComparison.Ordinal);
        Assert.Contains("docker-compose.log", script, StringComparison.Ordinal);
        Assert.Contains("KeepEnvironment", script, StringComparison.Ordinal);
        Assert.Contains("ReuseEnvironment", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeNodeLocalScriptIsDocumentedAsLocalOnly()
    {
        var scriptPath = Path.Combine(
            FindRepositoryRoot(),
            "scripts",
            "game",
            "ci",
            "test-agar-three-node.ps1");

        Assert.True(File.Exists(scriptPath), "The local Agar three-node script should exist.");
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("local-only", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("github.event", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GITHUB_ACTIONS", script, StringComparison.Ordinal);
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
