using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Agar.Unity.Tests;

public sealed class AgarThreeNodeLocalTestScriptTests
{
    private const string TargetTest = "SampleClient.Gameplay.Tests.DotArenaThreeNodePlayModeTests.UnityClientCompletesThreeNodeMultiplayerSmoke";

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
        Assert.Contains("Test-ReadinessEndpoint", script, StringComparison.Ordinal);
        Assert.Contains("data-1 ready", script, StringComparison.Ordinal);
        Assert.Contains("gateway-1 ready", script, StringComparison.Ordinal);
        Assert.Contains("battle-1 ready", script, StringComparison.Ordinal);
        Assert.Contains("Test-SharedActiveClusterView", script, StringComparison.Ordinal);
        Assert.Contains("$_.state -ne 'active'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Test-SharedReadyClusterView", script, StringComparison.Ordinal);
        Assert.Contains("Verify exact-incarnation gateway restart", script, StringComparison.Ordinal);
        Assert.Contains("Get-ActiveNodeIncarnation \"gateway-1\"", script, StringComparison.Ordinal);
        Assert.Contains("redis-cli --raw", script, StringComparison.Ordinal);
        Assert.DoesNotContain("lakona_membership_member", script, StringComparison.Ordinal);
        Assert.Contains("restart\", \"gateway-1", script, StringComparison.Ordinal);
        Assert.Contains("gateway-1 was fenced and replaced with a new incarnation", script, StringComparison.Ordinal);
        Assert.Contains("Verify Redis Membership Table contract", script, StringComparison.Ordinal);
        Assert.Contains("LAKONA_TEST_REDIS_CONNECTION", script, StringComparison.Ordinal);
        Assert.Contains("Verify whole-cluster crash recovery", script, StringComparison.Ordinal);
        Assert.Contains("all three crashed nodes rejoined with new incarnations", script, StringComparison.Ordinal);
        Assert.Contains("Run Unity PlayMode smoke after whole-cluster recovery", script, StringComparison.Ordinal);
        Assert.Contains("-TargetTest $defaultSmokeTest", script, StringComparison.Ordinal);
        Assert.Contains("Assert-NoStaleActorRegistryViewError", script, StringComparison.Ordinal);
        Assert.Contains("Actor registry has not observed the requested Membership view", script, StringComparison.Ordinal);
        Assert.Contains("ipv4_address: 10.10.0.2", script, StringComparison.Ordinal);
        Assert.Contains("ipv4_address: 10.10.0.3", script, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.10.0.1:21001", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Cluster__Peers", script, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.10.0.2:21002", script, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Endpoint: tcp://10.10.0.3:21003", script, StringComparison.Ordinal);
        Assert.Contains("Lakona__Endpoints: >-", script, StringComparison.Ordinal);
        Assert.Contains("\"AdvertisedHost\": \"127.0.0.1\"", script, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:26379:6379", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Lakona__Endpoints__0__AdvertisedHost", script, StringComparison.Ordinal);
        Assert.Contains("gateway-1", script, StringComparison.Ordinal);
        Assert.Contains("battle-1", script, StringComparison.Ordinal);
        Assert.Contains("-runTests", script, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("(?m)^\\s*\"-quit\",?\\s*$", RegexOptions.CultureInvariant), script);
        Assert.Contains("-testPlatform", script, StringComparison.Ordinal);
        Assert.Contains("PlayMode", script, StringComparison.Ordinal);
        Assert.Contains("TestResults.xml", script, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $testResults", script, StringComparison.Ordinal);
        Assert.Contains("assert-unity-test-results.ps1", script, StringComparison.Ordinal);
        Assert.Contains("-ResultsPath $testResults", script, StringComparison.Ordinal);
        Assert.Contains("-TargetTestName $TargetTest", script, StringComparison.Ordinal);
        Assert.Contains("SampleClient.Gameplay.Tests.DotArenaThreeNodePlayModeTests.UnityClientCompletesThreeNodeMultiplayerSmoke", script, StringComparison.Ordinal);
        Assert.Contains("$supportsTopologyChangeWindow", script, StringComparison.Ordinal);
        Assert.Contains("Run requested Unity PlayMode test", script, StringComparison.Ordinal);
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

    [Fact]
    public void ThreeNodeComposeUsesRedisMembershipTable()
    {
        var composePath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "Game.Unity.Agar",
            "docker-compose.yml");

        Assert.True(File.Exists(composePath), "The Agar Docker Compose file should exist.");
        var compose = File.ReadAllText(composePath);

        Assert.DoesNotContain("Lakona__Cluster__Peers:", compose, StringComparison.Ordinal);
        Assert.Contains("Lakona__Cluster__Membership__Provider: Redis", compose, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__LakonaClusterRedis", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void UnityResultValidatorAcceptsOnlyTheSingleExpectedPassedTest()
    {
        var result = RunUnityResultValidator(CreateTestRun());

        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [MemberData(nameof(InvalidUnityResultCases))]
    public void UnityResultValidatorRejectsInvalidOrUnexpectedResults(string? xml, string expectedMessage)
    {
        var result = RunUnityResultValidator(xml);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedMessage, result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TestResults.xml", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<string?, string> InvalidUnityResultCases()
    {
        var cases = new TheoryData<string?, string>
        {
            { null, "not created" },
            { "<test-run", "could not be parsed" },
            { "<results />", "test-run root" },
            { CreateTestRun(result: "Failed", passed: "0", failed: "1", targetResult: "Failed"), "result was" },
            { CreateTestRun(passed: "0", failed: "1"), "failed" },
            { CreateTestRun(passed: "0", skipped: "1"), "skipped" },
            { CreateTestRun(passed: "0", inconclusive: "1"), "inconclusive" },
            {
                CreateTestRun(
                    total: "2",
                    passed: "2",
                    testCases: $"<test-case fullname=\"{TargetTest}\" result=\"Passed\" /><test-case fullname=\"Other.Test\" result=\"Passed\" />"),
                "exactly one test"
            },
            { CreateTestRun(total: "0"), "exactly one test" },
            { CreateTestRun(passed: "0"), "exactly one test" },
            { CreateTestRun(total: "many"), "invalid 'total' count" },
            { CreateTestRun(testCases: "<test-case fullname=\"Other.Test\" result=\"Passed\" />"), "exactly one result" },
            {
                CreateTestRun(
                    testCases: $"<test-case fullname=\"{TargetTest}\" result=\"Passed\" /><test-case fullname=\"{TargetTest}\" result=\"Passed\" />"),
                "exactly one result"
            },
            { CreateTestRun(testCases: "<test-case fullname=\"Wrong.Namespace.UnityClientCompletesThreeNodeMultiplayerSmoke\" result=\"Passed\" />"), "exactly one result" },
            { CreateTestRun(targetResult: "Failed"), "target test" }
        };

        return cases;
    }

    private static string CreateTestRun(
        string result = "Passed",
        string total = "1",
        string passed = "1",
        string failed = "0",
        string skipped = "0",
        string inconclusive = "0",
        string targetResult = "Passed",
        string? testCases = null)
    {
        testCases ??= $"<test-case fullname=\"{TargetTest}\" result=\"{targetResult}\" />";
        return $"<test-run result=\"{result}\" total=\"{total}\" passed=\"{passed}\" failed=\"{failed}\" skipped=\"{skipped}\" inconclusive=\"{inconclusive}\">{testCases}</test-run>";
    }

    private static (int ExitCode, string Output) RunUnityResultValidator(string? xml)
    {
        var repositoryRoot = FindRepositoryRoot();
        var validatorPath = Path.Combine(repositoryRoot, "scripts", "game", "ci", "assert-unity-test-results.ps1");
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"lakona-agar-unity-results-{Guid.NewGuid():N}");
        var resultsPath = Path.Combine(tempDirectory, "TestResults.xml");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            if (xml is not null)
            {
                File.WriteAllText(resultsPath, xml);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(validatorPath);
            startInfo.ArgumentList.Add("-ResultsPath");
            startInfo.ArgumentList.Add(resultsPath);
            startInfo.ArgumentList.Add("-TargetTestName");
            startInfo.ArgumentList.Add(TargetTest);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pwsh.");
            var standardOutput = process.StandardOutput.ReadToEnd();
            var standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, standardOutput + standardError);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
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
