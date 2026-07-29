using System.Diagnostics;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests;

public sealed class E2eScriptRepositoryTests
{
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
