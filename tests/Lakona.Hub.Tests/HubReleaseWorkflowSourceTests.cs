using Xunit;
using System.Diagnostics;
using System.Text.Json;

namespace Lakona.Hub.Tests;

public sealed class HubReleaseWorkflowSourceTests
{
    [Fact]
    public void Workflow_PublishesThreeOperatingSystemsWithBundledSdkAndReleaseManifest()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-hub.yml"));
        var packager = File.ReadAllText(Path.Combine(root, "scripts", "hub", "New-HubRelease.ps1"));
        var project = File.ReadAllText(Path.Combine(root, "src", "Lakona.Hub", "Lakona.Hub.csproj"));

        Assert.Contains("win-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("linux-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("osx-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", workflow, StringComparison.Ordinal);
        Assert.Contains("Publish NativeAOT app with analysis warnings as errors", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:ILLinkTreatWarningsAsErrors=true", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:SuppressTrimAnalysisWarnings=false", workflow, StringComparison.Ordinal);
        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("ubuntu-22.04", workflow, StringComparison.Ordinal);
        Assert.Contains("macos-15-intel", workflow, StringComparison.Ordinal);
        Assert.Contains("macos-15", workflow, StringComparison.Ordinal);
        Assert.Contains("--aot-smoke-test", workflow, StringComparison.Ordinal);
        Assert.Contains($"DOTNET_SDK_VERSION: {HubRuntimeInfo.BundledDotNetSdkVersion}", workflow, StringComparison.Ordinal);
        Assert.Contains("-Filter '*.pdb'", workflow, StringComparison.Ordinal);
        Assert.Contains("New-HubRelease.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("New-HubLinuxPackages.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("github.com/goreleaser/nfpm/v2/cmd/nfpm@v2.47.0", workflow, StringComparison.Ordinal);
        Assert.Contains("hub-delta.json", packager, StringComparison.Ordinal);
        Assert.Contains("lakona-hub-manifest.json", packager, StringComparison.Ordinal);
        Assert.Contains("<PublishAot>true</PublishAot>", project, StringComparison.Ordinal);
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", project, StringComparison.Ordinal);
        Assert.Contains("<ILLinkTreatWarningsAsErrors>true</ILLinkTreatWarningsAsErrors>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_PublishesMainFromTheProjectVersionAndRejectsDuplicates()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "publish-hub.yml"));

        Assert.Contains("branches:", workflow, StringComparison.Ordinal);
        Assert.Contains("- main", workflow, StringComparison.Ordinal);
        Assert.Contains("src/Lakona.Hub/**", workflow, StringComparison.Ordinal);
        Assert.Contains("src/Lakona.ProjectSystem/**", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/hub/**", workflow, StringComparison.Ordinal);
        Assert.Contains("Lakona Hub releases must be published from main", workflow, StringComparison.Ordinal);
        Assert.Contains("Select-Xml -Path 'src/Lakona.Hub/Lakona.Hub.csproj'", workflow, StringComparison.Ordinal);
        Assert.Contains("Reject an already-published version", workflow, StringComparison.Ordinal);
        Assert.Contains("Bump src/Lakona.Hub/Lakona.Hub.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/Lakona.Hub.Tests/Lakona.Hub.Tests.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("--filter HubVersion", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:Version=${{ needs.validate.outputs.version }}", workflow, StringComparison.Ordinal);
        Assert.Contains("--target '${{ github.sha }}'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("inputs.version", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("github.ref_name", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release upload", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Packager_CreatesFullPackagesThenDeltaPackagesFromPreviousRelease()
    {
        var root = FindRepositoryRoot();
        var temporary = Path.Combine(Path.GetTempPath(), $"lakona-hub-packager-{Guid.NewGuid():N}");
        var publish = Path.Combine(temporary, "publish");
        var first = Path.Combine(temporary, "first");
        var second = Path.Combine(temporary, "second");
        try
        {
            foreach (var rid in new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" })
            {
                var platform = Path.Combine(publish, $"hub-{rid}");
                var sdk = Path.Combine(platform, "dotnet");
                Directory.CreateDirectory(sdk);
                await File.WriteAllTextAsync(
                    Path.Combine(platform, rid == "win-x64" ? "Lakona.Hub.exe" : "Lakona.Hub"),
                    "app-v1",
                    TestContext.Current.CancellationToken);
                await File.WriteAllTextAsync(
                    Path.Combine(sdk, rid == "win-x64" ? "dotnet.exe" : "dotnet"),
                    "sdk",
                    TestContext.Current.CancellationToken);
            }

            WriteLinuxPackages(publish, "1.0.0");

            await RunPackagerAsync(root, publish, first, "1.0.0", null);
            await File.WriteAllTextAsync(
                Path.Combine(publish, "hub-win-x64", "Lakona.Hub.exe"),
                "app-v2",
                TestContext.Current.CancellationToken);
            WriteLinuxPackages(publish, "1.1.0");
            await RunPackagerAsync(root, publish, second, "1.1.0", first);

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(second, "lakona-hub-manifest.json"),
                TestContext.Current.CancellationToken));
            var platforms = manifest.RootElement.GetProperty("platforms");
            Assert.Equal(5, platforms.EnumerateObject().Count());
            foreach (var platform in platforms.EnumerateObject())
            {
                Assert.True(File.Exists(Path.Combine(second, platform.Value.GetProperty("full").GetProperty("assetName").GetString()!)));
                if (platform.Name.StartsWith("linux-", StringComparison.Ordinal))
                {
                    Assert.Empty(platform.Value.GetProperty("deltas").EnumerateArray());
                }
                else
                {
                    var delta = Assert.Single(platform.Value.GetProperty("deltas").EnumerateArray());
                    Assert.Equal("1.0.0", delta.GetProperty("fromVersion").GetString());
                    Assert.True(File.Exists(Path.Combine(second, delta.GetProperty("assetName").GetString()!)));
                }
            }
            Assert.EndsWith(".deb", platforms.GetProperty("linux-x64-deb").GetProperty("full").GetProperty("assetName").GetString(), StringComparison.Ordinal);
            Assert.EndsWith(".rpm", platforms.GetProperty("linux-x64-rpm").GetProperty("full").GetProperty("assetName").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static void WriteLinuxPackages(string publish, string version)
    {
        var packages = Path.Combine(publish, "hub-linux-packages");
        Directory.CreateDirectory(packages);
        File.WriteAllText(Path.Combine(packages, $"lakona-hub_{version}_amd64.deb"), $"deb-{version}");
        File.WriteAllText(Path.Combine(packages, $"lakona-hub-{version}-1.x86_64.rpm"), $"rpm-{version}");
    }

    private static async Task RunPackagerAsync(
        string root,
        string publish,
        string output,
        string version,
        string? previous)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = root
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-File", Path.Combine(root, "scripts", "hub", "New-HubRelease.ps1"),
            "-Version", version,
            "-Tag", $"hub-v{version}",
            "-Repository", "bruce48x/Lakona",
            "-PublishRoot", publish,
            "-OutputRoot", output
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        if (previous is not null)
        {
            startInfo.ArgumentList.Add("-PreviousRoot");
            startInfo.ArgumentList.Add(previous);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var outputText = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errorText = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, $"Packager failed. Output: {outputText}{Environment.NewLine}Error: {errorText}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
