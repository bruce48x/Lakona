using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Lakona.Tool.Hotfix;
using Xunit;

namespace Lakona.Tool.Tests.Hotfix;

public sealed class HotfixPackageWriterTests
{
    [Fact]
    public async Task WritePackageAsync_creates_manifest_and_checksums()
    {
        var root = CreateTempRoot();
        try
        {
            var buildOutput = Path.Combine(root, "build");
            var packages = Path.Combine(root, "packages");
            Directory.CreateDirectory(buildOutput);
            await File.WriteAllTextAsync(Path.Combine(buildOutput, "Server.Hotfix.dll"), "dll", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(buildOutput, "Server.Hotfix.deps.json"), "{}", TestContext.Current.CancellationToken);

            var zipPath = await new HotfixPackageWriter().WritePackageAsync(
                buildOutput,
                packages,
                "Server.Hotfix",
                "net10.0",
                "20260612.001",
                "v20260612-153045Z",
                new DateTimeOffset(2026, 6, 12, 15, 30, 45, TimeSpan.Zero),
                TestContext.Current.CancellationToken);

            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "hotfix.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "checksums.sha256");

            var manifestEntry = archive.GetEntry("hotfix.json")!;
            await using var manifestStream = manifestEntry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<HotfixPackageManifest>(
                manifestStream,
                HotfixJson.Options,
                TestContext.Current.CancellationToken);
            Assert.Equal("v20260612-153045Z", manifest?.Version);
            Assert.Equal("Server.Hotfix.dll", manifest?.Assembly);
            Assert.Equal("20260612.001", manifest?.BuildTag);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_writes_ready_and_rejects_changed_existing_version()
    {
        var root = CreateTempRoot();
        try
        {
            var writer = new HotfixPackageWriter();
            var firstBuild = Path.Combine(root, "first-build");
            var secondBuild = Path.Combine(root, "second-build");
            var packages = Path.Combine(root, "packages");
            var installRoot = Path.Combine(root, "hotfix");
            Directory.CreateDirectory(firstBuild);
            Directory.CreateDirectory(secondBuild);
            await File.WriteAllTextAsync(Path.Combine(firstBuild, "Server.Hotfix.dll"), "first", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(secondBuild, "Server.Hotfix.dll"), "second", TestContext.Current.CancellationToken);

            var firstZip = await writer.WritePackageAsync(
                firstBuild,
                packages,
                "Server.Hotfix",
                "net10.0",
                "tag",
                "v20260612-153045Z",
                DateTimeOffset.UtcNow,
                TestContext.Current.CancellationToken);
            var version = await new HotfixPackageInstaller().InstallAsync(firstZip, installRoot, TestContext.Current.CancellationToken);
            Assert.Equal("v20260612-153045Z", version);
            Assert.True(File.Exists(Path.Combine(installRoot, "versions", version, "READY")));

            var reinstalled = await new HotfixPackageInstaller().InstallAsync(firstZip, installRoot, TestContext.Current.CancellationToken);
            Assert.Equal(version, reinstalled);

            var secondZip = await writer.WritePackageAsync(
                secondBuild,
                packages,
                "Server.Hotfix",
                "net10.0",
                "tag",
                "v20260612-153045Z",
                DateTimeOffset.UtcNow,
                TestContext.Current.CancellationToken);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new HotfixPackageInstaller().InstallAsync(secondZip, installRoot, TestContext.Current.CancellationToken));
            Assert.Contains("different content", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(@"..\outside")]
    [InlineData("nested/version")]
    public async Task InstallAsync_rejects_manifest_version_that_escapes_hotfix_root(string version)
    {
        var root = CreateTempRoot();
        try
        {
            var zip = await WritePackageWithVersionAsync(root, version);
            var installRoot = Path.Combine(root, "hotfix");

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await new HotfixPackageInstaller().InstallAsync(zip, installRoot, TestContext.Current.CancellationToken));

            Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(root, "outside")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_rejects_absolute_manifest_version()
    {
        var root = CreateTempRoot();
        try
        {
            var absoluteVersion = Path.Combine(Path.GetTempPath(), "LakonaHotfixOutside", Guid.NewGuid().ToString("N"));
            var zip = await WritePackageWithVersionAsync(root, absoluteVersion);
            var installRoot = Path.Combine(root, "hotfix");

            var exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await new HotfixPackageInstaller().InstallAsync(zip, installRoot, TestContext.Current.CancellationToken));

            Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(absoluteVersion));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_rejects_empty_checksum_file()
    {
        var root = CreateTempRoot();
        try
        {
            var zip = await WritePackageWithChecksumLinesAsync(root, []);
            var installRoot = Path.Combine(root, "hotfix");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new HotfixPackageInstaller().InstallAsync(zip, installRoot, TestContext.Current.CancellationToken));

            Assert.Contains("empty", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_rejects_checksum_file_that_omits_manifest_assembly()
    {
        var root = CreateTempRoot();
        try
        {
            var staging = await CreatePackageStagingAsync(root, "v20260612-153045Z");
            var hotfixHash = await Sha256Async(Path.Combine(staging, "hotfix.json"));
            var zip = await ZipPackageAsync(root, staging, [$"{hotfixHash} hotfix.json"]);
            var installRoot = Path.Combine(root, "hotfix");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new HotfixPackageInstaller().InstallAsync(zip, installRoot, TestContext.Current.CancellationToken));

            Assert.Contains("Server.Hotfix.dll", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("aabbcc ..\\outside.dll")]
    [InlineData("aabbcc ../outside.dll")]
    public async Task InstallAsync_rejects_checksum_paths_outside_package_directory(string line)
    {
        var root = CreateTempRoot();
        try
        {
            var zip = await WritePackageWithChecksumLinesAsync(root, [line]);
            var installRoot = Path.Combine(root, "hotfix");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new HotfixPackageInstaller().InstallAsync(zip, installRoot, TestContext.Current.CancellationToken));

            Assert.Contains("path", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_rejects_duplicate_checksum_entries()
    {
        var root = CreateTempRoot();
        try
        {
            var staging = await CreatePackageStagingAsync(root, "v20260612-153045Z");
            var dllHash = await Sha256Async(Path.Combine(staging, "Server.Hotfix.dll"));
            var zip = await ZipPackageAsync(root, staging, [$"{dllHash} Server.Hotfix.dll", $"{dllHash} Server.Hotfix.dll"]);
            var installRoot = Path.Combine(root, "hotfix");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new HotfixPackageInstaller().InstallAsync(zip, installRoot, TestContext.Current.CancellationToken));

            Assert.Contains("Duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LakonaHotfixPackageWriterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<string> WritePackageWithVersionAsync(string root, string version)
    {
        var staging = await CreatePackageStagingAsync(root, version);

        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(staging).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var hash = await Sha256Async(file);
            lines.Add($"{hash} {Path.GetFileName(file)}");
        }

        return await ZipPackageAsync(root, staging, lines);
    }

    private static async Task<string> WritePackageWithChecksumLinesAsync(string root, IReadOnlyList<string> checksumLines)
    {
        var staging = await CreatePackageStagingAsync(root, "v20260612-153045Z");
        return await ZipPackageAsync(root, staging, checksumLines);
    }

    private static async Task<string> CreatePackageStagingAsync(string root, string version)
    {
        var staging = Path.Combine(root, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        var manifest = new HotfixPackageManifest(
            version,
            DateTimeOffset.UtcNow,
            "Server.Hotfix.dll",
            "net10.0",
            "tag",
            "test");
        await File.WriteAllTextAsync(
            Path.Combine(staging, "hotfix.json"),
            JsonSerializer.Serialize(manifest, HotfixJson.Options),
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(staging, "Server.Hotfix.dll"), "dll", TestContext.Current.CancellationToken);
        return staging;
    }

    private static async Task<string> ZipPackageAsync(string root, string staging, IReadOnlyList<string> checksumLines)
    {
        var packages = Path.Combine(root, "packages");
        Directory.CreateDirectory(packages);
        await File.WriteAllLinesAsync(Path.Combine(staging, "checksums.sha256"), checksumLines, TestContext.Current.CancellationToken);

        var zip = Path.Combine(packages, "package.zip");
        if (File.Exists(zip))
        {
            File.Delete(zip);
        }

        ZipFile.CreateFromDirectory(staging, zip);
        Directory.Delete(staging, recursive: true);
        return zip;
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken)).ToLowerInvariant();
    }
}
