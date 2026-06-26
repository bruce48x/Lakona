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
        Assert.Contains("Lakona__Endpoints__0__AdvertisedHost", script, StringComparison.Ordinal);
        Assert.Contains("gateway-1", script, StringComparison.Ordinal);
        Assert.Contains("battle-1", script, StringComparison.Ordinal);
        Assert.True(
            script.Split("Lakona__Endpoints__0__AdvertisedHost", StringSplitOptions.None).Length - 1 >= 2,
            "The script should override advertised host for both gateway and battle endpoints.");
        Assert.Contains("-runTests", script, StringComparison.Ordinal);
        Assert.Contains("-testPlatform", script, StringComparison.Ordinal);
        Assert.Contains("PlayMode", script, StringComparison.Ordinal);
        Assert.Contains("TestResults.xml", script, StringComparison.Ordinal);
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
