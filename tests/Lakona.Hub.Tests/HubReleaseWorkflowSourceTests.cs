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
        Assert.Contains("New-HubWindowsPackage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("New-HubMacPackage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("New-HubLinuxPackages.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("wix --version 5.0.2", workflow, StringComparison.Ordinal);
        Assert.Contains("hdiutil attach", workflow, StringComparison.Ordinal);
        Assert.Contains("github.com/goreleaser/nfpm/v2/cmd/nfpm@v2.47.0", workflow, StringComparison.Ordinal);
        Assert.Contains(".msi", packager, StringComparison.Ordinal);
        Assert.Contains(".dmg", packager, StringComparison.Ordinal);
        Assert.Contains("linux-x64.deb", packager, StringComparison.Ordinal);
        Assert.Contains("linux-x64.rpm", packager, StringComparison.Ordinal);
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
    public void LinuxPackageConfig_ReferencesExistingRepositoryFiles()
    {
        var root = FindRepositoryRoot();
        var configPath = Path.Combine(root, "scripts", "hub", "linux", "nfpm.yaml");
        var configLines = File.ReadAllLines(configPath);

        var repositorySources = configLines
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- src: ", StringComparison.Ordinal))
            .Select(line => line[7..].Trim())
            .Where(source => !source.Contains("${", StringComparison.Ordinal))
            .Where(source => !Path.IsPathRooted(source));

        foreach (var source in repositorySources)
        {
            Assert.True(
                File.Exists(Path.Combine(root, source)),
                $"The Linux package source does not exist: {source}");
        }
    }

    [Fact]
    public async Task Packager_CreatesInstallerManifestForEveryPlatform()
    {
        var root = FindRepositoryRoot();
        var temporary = Path.Combine(Path.GetTempPath(), $"lakona-hub-packager-{Guid.NewGuid():N}");
        var publish = Path.Combine(temporary, "publish");
        var output = Path.Combine(temporary, "output");
        try
        {
            WriteInstallerPackages(publish, "1.1.0");
            await RunPackagerAsync(root, publish, output, "1.1.0");

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(output, "lakona-hub-manifest.json"),
                TestContext.Current.CancellationToken));
            var platforms = manifest.RootElement.GetProperty("platforms");
            Assert.Equal(5, platforms.EnumerateObject().Count());
            foreach (var platform in platforms.EnumerateObject())
            {
                Assert.True(File.Exists(Path.Combine(output, platform.Value.GetProperty("full").GetProperty("assetName").GetString()!)));
                Assert.Empty(platform.Value.GetProperty("deltas").EnumerateArray());
            }
            Assert.EndsWith(".msi", platforms.GetProperty("win-x64").GetProperty("full").GetProperty("assetName").GetString(), StringComparison.Ordinal);
            Assert.EndsWith(".dmg", platforms.GetProperty("osx-x64").GetProperty("full").GetProperty("assetName").GetString(), StringComparison.Ordinal);
            Assert.EndsWith(".dmg", platforms.GetProperty("osx-arm64").GetProperty("full").GetProperty("assetName").GetString(), StringComparison.Ordinal);
            Assert.Contains("linux", platforms.GetProperty("linux-x64-deb").GetProperty("full").GetProperty("assetName").GetString(), StringComparison.Ordinal);
            Assert.Contains("linux", platforms.GetProperty("linux-x64-rpm").GetProperty("full").GetProperty("assetName").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static void WriteInstallerPackages(string publish, string version)
    {
        var files = new Dictionary<string, string>
        {
            ["hub-win-x64-package"] = $"lakona-hub-{version}-win-x64.msi",
            ["hub-osx-x64-package"] = $"lakona-hub-{version}-osx-x64.dmg",
            ["hub-osx-arm64-package"] = $"lakona-hub-{version}-osx-arm64.dmg"
        };
        foreach (var (directory, file) in files)
        {
            var root = Path.Combine(publish, directory);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, file), directory);
        }

        var linux = Path.Combine(publish, "hub-linux-packages");
        Directory.CreateDirectory(linux);
        File.WriteAllText(Path.Combine(linux, $"lakona-hub-{version}-linux-x64.deb"), $"deb-{version}");
        File.WriteAllText(Path.Combine(linux, $"lakona-hub-{version}-linux-x64.rpm"), $"rpm-{version}");
    }

    private static async Task RunPackagerAsync(
        string root,
        string publish,
        string output,
        string version)
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
