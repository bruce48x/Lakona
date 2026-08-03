using System.Diagnostics;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class E2eScriptRepositoryTests
{
    [Fact]
    public void LocalFeed_builds_release_outputs_before_packing_without_rebuilding()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var scriptPath = Path.Combine(
            repositoryRoot,
            ".agents",
            "skills",
            "lakona-e2e-testing",
            "scripts",
            "run-e2e.ps1");
        var script = File.ReadAllText(scriptPath);
        const string buildCommand =
            "dotnet build $packSolution -c Release --nologo -v q";
        const string packCommand =
            "dotnet pack $packSolution -c Release -o $feedDir --no-build --no-restore --nologo -v q";

        var buildIndex = script.IndexOf(buildCommand, StringComparison.Ordinal);
        var packIndex = script.IndexOf(packCommand, StringComparison.Ordinal);

        Assert.True(buildIndex >= 0, $"Missing LocalFeed build command: {buildCommand}");
        Assert.True(packIndex >= 0, $"Missing LocalFeed pack command: {packCommand}");
        Assert.True(buildIndex < packIndex, "LocalFeed must finish its Release build before packing.");
        Assert.Contains(
            "SelectNodes(\"/Project/ItemGroup/PackageInputProject\")",
            script,
            StringComparison.Ordinal);
        Assert.Contains("foreach ($project in $buildProjects)", script, StringComparison.Ordinal);
        Assert.Contains(
            "foreach ($localFeedPath in @($feedDir, $packageCache))",
            script,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectReference_adapter_preserves_bundled_Hotfix_inputs()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var contractScript = Path.Combine(
            repositoryRoot,
            "tests",
            "Scripts",
            "test-lakona-e2e-script.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(contractScript);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start pwsh.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(
            process.ExitCode == 0,
            $"E2E script contract failed.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        Assert.Contains("Lakona scaffold E2E script contract: PASS", standardOutput, StringComparison.Ordinal);
    }
}
