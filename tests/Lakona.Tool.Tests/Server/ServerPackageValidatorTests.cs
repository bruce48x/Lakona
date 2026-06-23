using System.Security.Cryptography;
using System.Text.Json;
using Lakona.Tool.Hotfix;
using Lakona.Tool.Server;
using Xunit;

namespace Lakona.Tool.Tests.Server;

public sealed class ServerPackageValidatorTests
{
    private const string BuildTag = "20260612.001";
    private const string InitialHotfixVersion = "v20260623-153045Z";

    [Fact]
    public async Task ValidateAsync_rejects_missing_ready_file()
    {
        var fixture = await CreateValidServerPackageAsync(writeReady: false);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(
                    fixture.Root,
                    fixture.Manifest,
                    TestContext.Current.CancellationToken));

            Assert.Contains("READY", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_mismatched_build_tag()
    {
        var fixture = await CreateValidServerPackageAsync(hotfixBuildTag: "20260612.mismatch");
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(
                    fixture.Root,
                    fixture.Manifest,
                    TestContext.Current.CancellationToken));

            Assert.Contains("BuildTag", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_invalid_hotfix_checksum()
    {
        var fixture = await CreateValidServerPackageAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "hotfix", "versions", InitialHotfixVersion, "Server.Hotfix.dll"),
                "tampered",
                TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(
                    fixture.Root,
                    fixture.Manifest,
                    TestContext.Current.CancellationToken));

            Assert.Contains("Checksum mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_initial_hotfix_version_that_escapes_versions_directory()
    {
        var escapingVersion = @"..\..\outside";
        var manifest = CreateServerManifest(escapingVersion);
        var fixture = await CreateValidServerPackageAsync(
            manifest: manifest,
            hotfixVersion: escapingVersion);
        try
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(
                async () => await new ServerPackageValidator().ValidateAsync(
                    fixture.Root,
                    fixture.Manifest,
                    TestContext.Current.CancellationToken));

            Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_invalid_server_manifest_json()
    {
        var fixture = await CreateValidServerPackageAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "lakona-server.json"),
                "{",
                TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(
                    fixture.Root,
                    fixture.Manifest,
                    TestContext.Current.CancellationToken));

            Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_stale_server_manifest_file()
    {
        var fixture = await CreateValidServerPackageAsync();
        try
        {
            var staleManifest = CreateServerManifest(
                InitialHotfixVersion,
                buildTag: "20260612.stale");
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "lakona-server.json"),
                JsonSerializer.Serialize(staleManifest, ServerJson.Options),
                TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(
                    fixture.Root,
                    fixture.Manifest,
                    TestContext.Current.CancellationToken));

            Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    public async Task ValidateAsync_rejects_build_output_directories(string directoryName)
    {
        var fixture = await CreateValidServerPackageAsync();
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture.Root, "nested", directoryName));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(
                    fixture.Root,
                    fixture.Manifest,
                    TestContext.Current.CancellationToken));

            Assert.Contains(directoryName, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_accepts_valid_server_package_tree()
    {
        var fixture = await CreateValidServerPackageAsync();
        try
        {
            await new ServerPackageValidator().ValidateAsync(
                fixture.Root,
                fixture.Manifest,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private static async Task<ServerPackageFixture> CreateValidServerPackageAsync(
        bool writeReady = true,
        string hotfixBuildTag = BuildTag,
        string hotfixVersion = InitialHotfixVersion,
        ServerPackageManifest? manifest = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "LakonaServerPackageValidatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        manifest ??= CreateServerManifest(InitialHotfixVersion);

        await File.WriteAllTextAsync(
            Path.Combine(root, "Server.App.dll"),
            "server dll",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "appsettings.json"),
            "{}",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "lakona-server.json"),
            JsonSerializer.Serialize(manifest, ServerJson.Options),
            TestContext.Current.CancellationToken);

        var hotfixRoot = Path.Combine(root, "hotfix");
        Directory.CreateDirectory(hotfixRoot);
        await File.WriteAllTextAsync(
            Path.Combine(hotfixRoot, "current.txt"),
            manifest.InitialHotfixVersion,
            TestContext.Current.CancellationToken);

        var versionDirectory = Path.Combine(hotfixRoot, "versions", hotfixVersion);
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "Server.Hotfix.dll"),
            "hotfix dll",
            TestContext.Current.CancellationToken);

        var hotfixManifest = new HotfixPackageManifest(
            hotfixVersion,
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero),
            "Server.Hotfix.dll",
            "net10.0",
            hotfixBuildTag,
            "0.14.0-test");
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "hotfix.json"),
            JsonSerializer.Serialize(hotfixManifest, HotfixJson.Options),
            TestContext.Current.CancellationToken);

        if (writeReady)
        {
            await File.WriteAllTextAsync(
                Path.Combine(versionDirectory, "READY"),
                "",
                TestContext.Current.CancellationToken);
        }

        await WriteChecksumsAsync(versionDirectory);
        return new ServerPackageFixture(root, manifest);
    }

    private static ServerPackageManifest CreateServerManifest(
        string initialHotfixVersion,
        string buildTag = BuildTag)
    {
        return ServerPackageManifest.CreateV1(
            "v20260623-153045Z",
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero),
            "linux-x64",
            "Release",
            "Server.App.dll",
            buildTag,
            initialHotfixVersion,
            "0.14.0-test");
    }

    private static async Task WriteChecksumsAsync(string directory)
    {
        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            if (StringComparer.Ordinal.Equals(Path.GetFileName(file), "checksums.sha256"))
            {
                continue;
            }

            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken)).ToLowerInvariant();
            lines.Add($"{hash} {Path.GetFileName(file)}");
        }

        await File.WriteAllLinesAsync(
            Path.Combine(directory, "checksums.sha256"),
            lines,
            TestContext.Current.CancellationToken);
    }

    private sealed record ServerPackageFixture(string Root, ServerPackageManifest Manifest);
}
