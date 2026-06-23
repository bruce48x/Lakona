using System.IO.Compression;
using System.Text.Json;
using Lakona.Tool.Hotfix;
using Lakona.Tool.Server;
using Xunit;

namespace Lakona.Tool.Tests.Server;

public sealed class ServerPackageWriterTests
{
    private const string Version = "v20260623-153045Z";
    private const string BuildTag = "20260623.001";

    [Fact]
    public async Task WritePackageFromPublishedAppAsync_creates_rooted_zip_with_installed_hotfix()
    {
        var root = CreateTempRoot();
        try
        {
            var publishedApp = await CreatePublishedAppAsync(root);
            var hotfixZip = await CreateHotfixPackageAsync(root, Version, BuildTag);
            var output = Path.Combine(root, "packages");
            var builtAtUtc = new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero);

            var zipPath = await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                new ServerPackageWriteRequest(
                    publishedApp,
                    hotfixZip,
                    output,
                    "Server.App.dll",
                    "linux-x64",
                    "Release",
                    Version,
                    BuildTag,
                    builtAtUtc),
                TestContext.Current.CancellationToken);

            Assert.Equal(Path.Combine(output, "Server.App-v20260623-153045Z-linux-x64.zip"), zipPath);
            Assert.True(File.Exists(zipPath));

            using var archive = ZipFile.OpenRead(zipPath);
            AssertEntryExists(archive, "Server.App.dll");
            AssertEntryExists(archive, "appsettings.json");
            AssertEntryExists(archive, "Server.App.runtimeconfig.json");
            AssertEntryExists(archive, "lakona-server.json");
            AssertEntryExists(archive, "hotfix/current.txt");
            AssertEntryExists(archive, "hotfix/versions/v20260623-153045Z/Server.Hotfix.dll");
            AssertEntryExists(archive, "hotfix/versions/v20260623-153045Z/hotfix.json");
            AssertEntryExists(archive, "hotfix/versions/v20260623-153045Z/checksums.sha256");
            AssertEntryExists(archive, "hotfix/versions/v20260623-153045Z/READY");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith("reload.signal", StringComparison.Ordinal));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("app/", StringComparison.Ordinal));

            var manifestEntry = archive.GetEntry("lakona-server.json")!;
            await using var manifestStream = manifestEntry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<ServerPackageManifest>(
                manifestStream,
                ServerJson.Options,
                TestContext.Current.CancellationToken);

            Assert.NotNull(manifest);
            Assert.Equal(Version, manifest.Version);
            Assert.Equal("linux-x64", manifest.Runtime);
            Assert.Equal("Release", manifest.Configuration);
            Assert.True(manifest.SelfContained);
            Assert.False(manifest.Trimmed);
            Assert.Equal("Server.App.dll", manifest.EntryAssembly);
            Assert.Equal(BuildTag, manifest.BuildTag);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WritePackageFromPublishedAppAsync_rejects_build_tag_mismatch()
    {
        var root = CreateTempRoot();
        try
        {
            var request = await CreateRequestAsync(root, Version, "app-tag", hotfixBuildTag: "hotfix-tag");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    request,
                    TestContext.Current.CancellationToken));

            Assert.Contains("BuildTag", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WritePackageFromPublishedAppAsync_rejects_hotfix_version_that_differs_from_server_version()
    {
        var root = CreateTempRoot();
        try
        {
            var request = await CreateRequestAsync(root, Version, BuildTag, hotfixVersion: "v20260624-153045Z");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    request,
                    TestContext.Current.CancellationToken));

            Assert.Contains("Initial hotfix version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<ServerPackageWriteRequest> CreateRequestAsync(
        string root,
        string version,
        string buildTag,
        string? hotfixVersion = null,
        string? hotfixBuildTag = null)
    {
        var publishedApp = await CreatePublishedAppAsync(root);
        var hotfixZip = await CreateHotfixPackageAsync(root, hotfixVersion ?? version, hotfixBuildTag ?? buildTag);
        return new ServerPackageWriteRequest(
            publishedApp,
            hotfixZip,
            Path.Combine(root, "packages"),
            "Server.App.dll",
            "linux-x64",
            "Release",
            version,
            buildTag,
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero));
    }

    private static async Task<string> CreatePublishedAppAsync(string root)
    {
        var publishedApp = Path.Combine(root, "published-app");
        Directory.CreateDirectory(publishedApp);
        await File.WriteAllTextAsync(Path.Combine(publishedApp, "Server.App.dll"), "server dll", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(publishedApp, "appsettings.json"), "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(publishedApp, "Server.App.runtimeconfig.json"), "{}", TestContext.Current.CancellationToken);
        return publishedApp;
    }

    private static async Task<string> CreateHotfixPackageAsync(string root, string version, string buildTag)
    {
        var buildOutput = Path.Combine(root, "hotfix-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(buildOutput);
        await File.WriteAllTextAsync(Path.Combine(buildOutput, "Server.Hotfix.dll"), "hotfix dll", TestContext.Current.CancellationToken);
        return await new HotfixPackageWriter().WritePackageAsync(
            buildOutput,
            Path.Combine(root, "hotfix-packages"),
            "Server.Hotfix",
            "net10.0",
            buildTag,
            version,
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LakonaServerPackageWriterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AssertEntryExists(ZipArchive archive, string entryName)
    {
        Assert.Contains(archive.Entries, entry => entry.FullName == entryName);
    }
}
