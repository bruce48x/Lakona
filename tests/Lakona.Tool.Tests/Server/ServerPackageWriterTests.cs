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
    public async Task WritePackageFromPublishedAppAsync_removes_preexisting_hotfix_directory_before_installing_hotfix()
    {
        var root = CreateTempRoot();
        try
        {
            var publishedApp = await CreatePublishedAppAsync(root);
            Directory.CreateDirectory(Path.Combine(publishedApp, "hotfix"));
            await File.WriteAllTextAsync(
                Path.Combine(publishedApp, "hotfix", "reload.signal"),
                "",
                TestContext.Current.CancellationToken);
            var hotfixZip = await CreateHotfixPackageAsync(root, Version, BuildTag);

            var zipPath = await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                CreateRequest(
                    publishedApp,
                    hotfixZip,
                    Path.Combine(root, "packages"),
                    Version,
                    BuildTag),
                TestContext.Current.CancellationToken);

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName == "hotfix/reload.signal");
            AssertEntryExists(archive, "hotfix/current.txt");
            AssertEntryExists(archive, "hotfix/versions/v20260623-153045Z/Server.Hotfix.dll");
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

    [Theory]
    [InlineData(@"..\outside")]
    [InlineData("linux/x64")]
    public async Task WritePackageFromPublishedAppAsync_rejects_runtime_identifier_with_path_separators(string runtimeIdentifier)
    {
        var root = CreateTempRoot();
        try
        {
            var request = await CreateRequestAsync(root, Version, BuildTag, runtimeIdentifier: runtimeIdentifier);

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    request,
                    TestContext.Current.CancellationToken));

            Assert.Contains("RuntimeIdentifier", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"..\v20260623-153045Z")]
    [InlineData("v20260623/153045Z")]
    public async Task WritePackageFromPublishedAppAsync_rejects_version_with_path_separators(string version)
    {
        var root = CreateTempRoot();
        try
        {
            var request = await CreateRequestAsync(root, version, BuildTag, hotfixVersion: Version);

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    request,
                    TestContext.Current.CancellationToken));

            Assert.Contains("Version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"..\Server.App.dll")]
    [InlineData("nested/Server.App.dll")]
    public async Task WritePackageFromPublishedAppAsync_rejects_entry_assembly_with_path_separators(string entryAssembly)
    {
        var root = CreateTempRoot();
        try
        {
            var request = await CreateRequestAsync(root, Version, BuildTag, entryAssembly: entryAssembly);

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    request,
                    TestContext.Current.CancellationToken));

            Assert.Contains("EntryAssembly", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ServerPackageWriter_rejects_unix_and_windows_separator_literals()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Lakona.Tool",
            "Server",
            "ServerPackageWriter.cs"));

        Assert.Contains("value.Contains('/')", source, StringComparison.Ordinal);
        Assert.Contains("value.Contains('\\\\')", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritePackageFromPublishedAppAsync_rejects_output_directory_inside_published_app()
    {
        var root = CreateTempRoot();
        try
        {
            var publishedApp = await CreatePublishedAppAsync(root);
            var hotfixZip = await CreateHotfixPackageAsync(root, Version, BuildTag);
            var request = CreateRequest(
                publishedApp,
                hotfixZip,
                Path.Combine(publishedApp, "packages"),
                Version,
                BuildTag);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    request,
                    TestContext.Current.CancellationToken));

            Assert.Contains("OutputDirectory", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WritePackageFromPublishedAppAsync_deletes_temporary_zip_when_replace_fails()
    {
        var root = CreateTempRoot();
        try
        {
            var request = await CreateRequestAsync(root, Version, BuildTag);
            Directory.CreateDirectory(Path.Combine(
                request.OutputDirectory,
                "Server.App-v20260623-153045Z-linux-x64.zip"));

            var exception = await Record.ExceptionAsync(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    request,
                    TestContext.Current.CancellationToken));

            Assert.NotNull(exception);
            Assert.Empty(Directory.GetFiles(request.OutputDirectory, "*.tmp"));
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
        string? hotfixBuildTag = null,
        string runtimeIdentifier = "linux-x64",
        string entryAssembly = "Server.App.dll")
    {
        var publishedApp = await CreatePublishedAppAsync(root);
        var hotfixZip = await CreateHotfixPackageAsync(root, hotfixVersion ?? version, hotfixBuildTag ?? buildTag);
        return CreateRequest(
            publishedApp,
            hotfixZip,
            Path.Combine(root, "packages"),
            version,
            buildTag,
            runtimeIdentifier,
            entryAssembly);
    }

    private static ServerPackageWriteRequest CreateRequest(
        string publishedApp,
        string hotfixZip,
        string outputDirectory,
        string version,
        string buildTag,
        string runtimeIdentifier = "linux-x64",
        string entryAssembly = "Server.App.dll")
    {
        return new ServerPackageWriteRequest(
            publishedApp,
            hotfixZip,
            outputDirectory,
            entryAssembly,
            runtimeIdentifier,
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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lakona.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root could not be found.");
    }

    private static void AssertEntryExists(ZipArchive archive, string entryName)
    {
        Assert.Contains(archive.Entries, entry => entry.FullName == entryName);
    }
}
