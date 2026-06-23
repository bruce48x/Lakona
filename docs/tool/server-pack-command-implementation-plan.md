# Server Pack Command Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This repository ignores `docs/superpowers/`, so this trackable plan is intentionally saved under `docs/tool/`.

**Goal:** Implement `lakona-tool server pack` so a generated Lakona.Game server can be packaged as one self-contained, RID-specific, untrimmed zip with an installed initial hotfix version.

**Architecture:** Add a new `server` command family beside the existing `hotfix` command family. Keep publishing orchestration in `Lakona.Tool.Server`, reuse existing hotfix package writer and installer behavior, and test most packaging behavior without running a real `dotnet publish`.

**Tech Stack:** C# 13 / .NET 10, `System.Diagnostics.Process`, `System.IO.Compression.ZipFile`, `System.Text.Json`, xUnit v3, existing `Lakona.Tool` CLI and hotfix package code.

---

## Source Context

Read these files before editing code:

- `CONTRIBUTING.md`
- `docs/tool/server-pack-command.md`
- `docs/tool/generation-architecture.md`
- `src/Lakona.Tool/Cli/CliApplication.cs`
- `src/Lakona.Tool/Cli/Commands/Hotfix/HotfixCommand.cs`
- `src/Lakona.Tool/Cli/Commands/Hotfix/HotfixPackCommand.cs`
- `src/Lakona.Tool/Hotfix/HotfixPackageWriter.cs`
- `src/Lakona.Tool/Hotfix/HotfixPackageInstaller.cs`
- `src/Lakona.Tool/Rendering/Docs/GeneratedProjectGuideRenderer.cs`
- `tests/Lakona.Tool.Tests/Hotfix/HotfixPackageWriterTests.cs`
- `tests/Lakona.Tool.Tests/Rendering/GeneratedProjectGuideRendererTests.cs`
- `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

Do not edit unrelated sample files. If `git status --short` shows existing
changes outside this plan, leave them alone unless they block the task.

## Fixed Requirements

Implement these requirements exactly:

- Command shape: `lakona-tool server pack --runtime <rid>`.
- `--runtime` is required. Do not invent a runtime default.
- `--configuration` defaults to `Release` and accepts any non-empty string.
- `Debug` must work because the option is passed through, not because of an
  enum special case.
- `--project` defaults to `Server/App/Server.App.csproj`.
- `--hotfix-project` defaults to `Server/Hotfix/Server.Hotfix.csproj`.
- `--output` defaults to `artifacts/server`.
- `--version` defaults to a UTC timestamp formatted as
  `vyyyyMMdd-HHmmssZ`, for example `v20260623-153045Z`.
- `dotnet publish` must receive `--self-contained true`.
- `dotnet publish` must receive `-r <runtime>`.
- Do not expose `--self-contained`.
- Do not expose `--trim`.
- Do not pass `PublishTrimmed`, `PublishSingleFile`, or NativeAOT properties.
- The final artifact is one zip file.
- The zip root is the published application root, not an extra wrapper folder.
- The zip must contain `lakona-server.json` at root.
- The zip must contain the production hotfix root shape:
  `hotfix/current.txt` and `hotfix/versions/<version>/READY`.
- The zip must not contain `reload.signal`.
- The server package version and initial hotfix version are the same in v1.
- The server manifest BuildTag must equal the initial hotfix manifest BuildTag.
- Custom `--project` and `--hotfix-project` paths must fail when their
  BuildTag values differ. Do not override the hotfix manifest BuildTag with the
  app BuildTag.
- Bump `src/Lakona.Tool/Lakona.Tool.csproj` from `0.13.0` to `0.14.0`.

## File Map

Create:

- `src/Lakona.Tool/Cli/Commands/Server/ServerCommand.cs`
- `src/Lakona.Tool/Cli/Commands/Server/ServerPackCommand.cs`
- `src/Lakona.Tool/Hotfix/HotfixPackageVerifier.cs`
- `src/Lakona.Tool/Server/DotNetCommandResult.cs`
- `src/Lakona.Tool/Server/DotNetCommandRunner.cs`
- `src/Lakona.Tool/Server/HotfixPackageBuilder.cs`
- `src/Lakona.Tool/Server/IDotNetCommandRunner.cs`
- `src/Lakona.Tool/Server/IHotfixPackageBuilder.cs`
- `src/Lakona.Tool/Server/IServerPackageWriter.cs`
- `src/Lakona.Tool/Server/ServerJson.cs`
- `src/Lakona.Tool/Server/ServerPackageManifest.cs`
- `src/Lakona.Tool/Server/ServerPackageValidator.cs`
- `src/Lakona.Tool/Server/ServerPackageWriteRequest.cs`
- `src/Lakona.Tool/Server/ServerPackageWriter.cs`
- `src/Lakona.Tool/Server/ServerPackOptions.cs`
- `tests/Lakona.Tool.Tests/Cli/ServerPackCommandTests.cs`
- `tests/Lakona.Tool.Tests/Server/ServerPackageManifestTests.cs`
- `tests/Lakona.Tool.Tests/Server/ServerPackageWriterTests.cs`
- `tests/Lakona.Tool.Tests/Server/ServerPackageValidatorTests.cs`

Modify:

- `src/Lakona.Tool/Cli/CliApplication.cs`
- `src/Lakona.Tool/Cli/Text/ToolText.cs`
- `src/Lakona.Tool/Hotfix/HotfixPackageInstaller.cs`
- `src/Lakona.Tool/Lakona.Tool.csproj`
- `src/Lakona.Tool/README.md`
- `src/Lakona.Tool/Rendering/Docs/GeneratedProjectGuideRenderer.cs`
- `docs/tool/generation-architecture.md`
- `docs/tool/server-pack-command.md`
- `tests/Lakona.Tool.Tests/Rendering/GeneratedProjectGuideRendererTests.cs`
- `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

Do not modify:

- `src/Lakona.Game.*`
- `src/Lakona.Rpc.*`
- `samples/**`
- `blog/**`

## Task 1: Server Manifest And Option Types

**Files:**

- Create: `src/Lakona.Tool/Server/ServerJson.cs`
- Create: `src/Lakona.Tool/Server/ServerPackageManifest.cs`
- Create: `src/Lakona.Tool/Server/ServerPackOptions.cs`
- Create: `src/Lakona.Tool/Server/ServerPackageWriteRequest.cs`
- Create: `src/Lakona.Tool/Server/UtcDateTimeOffsetJsonConverter.cs`
- Create: `tests/Lakona.Tool.Tests/Server/ServerPackageManifestTests.cs`

- [ ] **Step 1: Write the failing manifest serialization test**

Create `tests/Lakona.Tool.Tests/Server/ServerPackageManifestTests.cs`:

```csharp
using System.Text.Json;
using Lakona.Tool.Server;
using Xunit;

namespace Lakona.Tool.Tests.Server;

public sealed class ServerPackageManifestTests
{
    [Fact]
    public void Manifest_serializes_with_web_casing_and_fixed_v1_flags()
    {
        var manifest = new ServerPackageManifest(
            "v20260623-153045Z",
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero),
            "linux-x64",
            "Release",
            selfContained: true,
            trimmed: false,
            "Server.App.dll",
            "20260612.001",
            "v20260623-153045Z",
            "0.14.0-test");

        var json = JsonSerializer.Serialize(manifest, ServerJson.Options);

        Assert.Contains("\"version\": \"v20260623-153045Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"builtAtUtc\": \"2026-06-23T15:30:45Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"runtime\": \"linux-x64\"", json, StringComparison.Ordinal);
        Assert.Contains("\"configuration\": \"Release\"", json, StringComparison.Ordinal);
        Assert.Contains("\"selfContained\": true", json, StringComparison.Ordinal);
        Assert.Contains("\"trimmed\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"entryAssembly\": \"Server.App.dll\"", json, StringComparison.Ordinal);
        Assert.Contains("\"buildTag\": \"20260612.001\"", json, StringComparison.Ordinal);
        Assert.Contains("\"initialHotfixVersion\": \"v20260623-153045Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"toolVersion\": \"0.14.0-test\"", json, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run the new test and verify it fails**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter ServerPackageManifestTests
```

Expected: fail because `Lakona.Tool.Server.ServerPackageManifest` and
`ServerJson` do not exist.

- [ ] **Step 3: Add the server package data types**

Create `src/Lakona.Tool/Server/ServerJson.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lakona.Tool.Server;

internal static class ServerJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };
        options.Converters.Add(new UtcDateTimeOffsetJsonConverter());
        return options;
    }
}
```

Create `src/Lakona.Tool/Server/UtcDateTimeOffsetJsonConverter.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lakona.Tool.Server;

internal sealed class UtcDateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString()
            ?? throw new JsonException("Expected UTC timestamp text.");
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture));
    }
}
```

Create `src/Lakona.Tool/Server/ServerPackageManifest.cs`:

```csharp
namespace Lakona.Tool.Server;

internal sealed record ServerPackageManifest(
    string Version,
    DateTimeOffset BuiltAtUtc,
    string Runtime,
    string Configuration,
    bool SelfContained,
    bool Trimmed,
    string EntryAssembly,
    string BuildTag,
    string InitialHotfixVersion,
    string ToolVersion);
```

Create `src/Lakona.Tool/Server/ServerPackOptions.cs`:

```csharp
namespace Lakona.Tool.Server;

internal sealed record ServerPackOptions(
    string ProjectPath,
    string HotfixProjectPath,
    string OutputDirectory,
    string RuntimeIdentifier,
    string Configuration,
    string Version);
```

Create `src/Lakona.Tool/Server/ServerPackageWriteRequest.cs`:

```csharp
namespace Lakona.Tool.Server;

internal sealed record ServerPackageWriteRequest(
    string PublishedAppDirectory,
    string HotfixPackagePath,
    string OutputDirectory,
    string EntryAssembly,
    string RuntimeIdentifier,
    string Configuration,
    string Version,
    string BuildTag,
    DateTimeOffset BuiltAtUtc);
```

- [ ] **Step 4: Run the test and verify it passes**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter ServerPackageManifestTests
```

Expected: pass.

- [ ] **Step 5: Commit checkpoint**

Run only if this implementation session is using commits:

```powershell
git add src/Lakona.Tool/Server/ServerJson.cs src/Lakona.Tool/Server/ServerPackageManifest.cs src/Lakona.Tool/Server/ServerPackOptions.cs src/Lakona.Tool/Server/ServerPackageWriteRequest.cs src/Lakona.Tool/Server/UtcDateTimeOffsetJsonConverter.cs tests/Lakona.Tool.Tests/Server/ServerPackageManifestTests.cs
git commit -m "feat(tool): add server package manifest types"
```

## Task 2: Server Package Validator

**Files:**

- Create: `src/Lakona.Tool/Hotfix/HotfixPackageVerifier.cs`
- Create: `src/Lakona.Tool/Server/ServerPackageValidator.cs`
- Modify: `src/Lakona.Tool/Hotfix/HotfixPackageInstaller.cs`
- Create: `tests/Lakona.Tool.Tests/Server/ServerPackageValidatorTests.cs`

- [ ] **Step 1: Write failing validator tests**

Create `tests/Lakona.Tool.Tests/Server/ServerPackageValidatorTests.cs`:

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using Lakona.Tool.Hotfix;
using Lakona.Tool.Server;
using Xunit;

namespace Lakona.Tool.Tests.Server;

public sealed class ServerPackageValidatorTests
{
    [Fact]
    public async Task ValidateAsync_rejects_missing_ready_file()
    {
        var root = CreateTempRoot();
        try
        {
            await CreateValidServerPackageTreeAsync(root, ready: false, hotfixBuildTag: "tag");
            var manifest = Manifest(buildTag: "tag");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(root, manifest, TestContext.Current.CancellationToken));

            Assert.Contains("READY", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_mismatched_build_tag()
    {
        var root = CreateTempRoot();
        try
        {
            await CreateValidServerPackageTreeAsync(root, ready: true, hotfixBuildTag: "hotfix-tag");
            var manifest = Manifest(buildTag: "server-tag");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(root, manifest, TestContext.Current.CancellationToken));

            Assert.Contains("BuildTag", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_rejects_invalid_hotfix_checksum()
    {
        var root = CreateTempRoot();
        try
        {
            await CreateValidServerPackageTreeAsync(root, ready: true, hotfixBuildTag: "tag");
            await File.WriteAllTextAsync(
                Path.Combine(root, "hotfix", "versions", "v20260623-153045Z", "Server.Hotfix.dll"),
                "changed",
                TestContext.Current.CancellationToken);
            var manifest = Manifest(buildTag: "tag");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(root, manifest, TestContext.Current.CancellationToken));

            Assert.Contains("Checksum mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    public async Task ValidateAsync_rejects_build_output_directories(string directoryName)
    {
        var root = CreateTempRoot();
        try
        {
            await CreateValidServerPackageTreeAsync(root, ready: true, hotfixBuildTag: "tag");
            Directory.CreateDirectory(Path.Combine(root, directoryName));
            var manifest = Manifest(buildTag: "tag");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageValidator().ValidateAsync(root, manifest, TestContext.Current.CancellationToken));

            Assert.Contains(directoryName, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAsync_accepts_valid_server_package_tree()
    {
        var root = CreateTempRoot();
        try
        {
            await CreateValidServerPackageTreeAsync(root, ready: true, hotfixBuildTag: "tag");
            var manifest = Manifest(buildTag: "tag");

            await new ServerPackageValidator().ValidateAsync(root, manifest, TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LakonaServerPackageValidatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ServerPackageManifest Manifest(string buildTag)
    {
        return new ServerPackageManifest(
            "v20260623-153045Z",
            DateTimeOffset.UtcNow,
            "linux-x64",
            "Release",
            selfContained: true,
            trimmed: false,
            "Server.App.dll",
            buildTag,
            "v20260623-153045Z",
            "test");
    }

    private static async Task CreateValidServerPackageTreeAsync(string root, bool ready, string hotfixBuildTag)
    {
        await File.WriteAllTextAsync(Path.Combine(root, "Server.App.dll"), "app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "appsettings.json"), "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root, "lakona-server.json"),
            JsonSerializer.Serialize(Manifest(hotfixBuildTag), ServerJson.Options),
            TestContext.Current.CancellationToken);

        var hotfixVersion = Path.Combine(root, "hotfix", "versions", "v20260623-153045Z");
        Directory.CreateDirectory(hotfixVersion);
        await File.WriteAllTextAsync(Path.Combine(root, "hotfix", "current.txt"), "v20260623-153045Z", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(hotfixVersion, "Server.Hotfix.dll"), "hotfix", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(hotfixVersion, "hotfix.json"),
            JsonSerializer.Serialize(
                new HotfixPackageManifest("v20260623-153045Z", DateTimeOffset.UtcNow, "Server.Hotfix.dll", "net10.0", hotfixBuildTag, "test"),
                HotfixJson.Options),
            TestContext.Current.CancellationToken);
        await WriteChecksumsAsync(hotfixVersion);
        if (ready)
        {
            await File.WriteAllTextAsync(Path.Combine(hotfixVersion, "READY"), "", TestContext.Current.CancellationToken);
        }
    }

    private static async Task WriteChecksumsAsync(string directory)
    {
        var lines = new List<string>();
        foreach (var file in Directory.GetFiles(directory).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (StringComparer.Ordinal.Equals(name, "checksums.sha256"))
            {
                continue;
            }

            await using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, TestContext.Current.CancellationToken)).ToLowerInvariant();
            lines.Add($"{hash} {name}");
        }

        await File.WriteAllLinesAsync(Path.Combine(directory, "checksums.sha256"), lines, TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 2: Run validator tests and verify they fail**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter ServerPackageValidatorTests
```

Expected: fail because `ServerPackageValidator` does not exist.

- [ ] **Step 3: Extract reusable hotfix checksum verification**

Create `src/Lakona.Tool/Hotfix/HotfixPackageVerifier.cs`:

```csharp
using System.Security.Cryptography;

namespace Lakona.Tool.Hotfix;

internal static class HotfixPackageVerifier
{
    public static async Task VerifyChecksumsAsync(
        string directory,
        string assemblyFileName,
        CancellationToken cancellationToken)
    {
        var checksumPath = Path.Combine(directory, "checksums.sha256");
        if (!File.Exists(checksumPath))
        {
            throw new InvalidOperationException("Hotfix package is missing checksums.sha256.");
        }

        var lines = await File.ReadAllLinesAsync(checksumPath, cancellationToken).ConfigureAwait(false);
        var checksums = ParseChecksums(directory, lines);
        RequireChecksum(checksums, "hotfix.json");
        RequireChecksum(checksums, assemblyFileName);

        foreach (var item in checksums.Values)
        {
            if (!File.Exists(item.FullPath))
            {
                throw new InvalidOperationException($"Hotfix package is missing '{item.RelativePath}'.");
            }

            await using var stream = File.OpenRead(item.FullPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
            if (!StringComparer.OrdinalIgnoreCase.Equals(item.Hash, actual))
            {
                throw new InvalidOperationException($"Checksum mismatch for '{item.RelativePath}'.");
            }
        }
    }

    private static Dictionary<string, ChecksumEntry> ParseChecksums(
        string directory,
        IReadOnlyList<string> lines)
    {
        var entries = new Dictionary<string, ChecksumEntry>(PathComparer);
        foreach (var line in lines.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new InvalidOperationException("Hotfix checksum file is invalid.");
            }

            var relativePath = NormalizeSeparators(parts[1]);
            if (Path.IsPathRooted(relativePath) || IsRootedWithAnySeparator(relativePath))
            {
                throw new InvalidOperationException("Hotfix checksum path is invalid.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
            if (!IsUnderDirectory(directory, fullPath))
            {
                throw new InvalidOperationException("Hotfix checksum path is invalid.");
            }

            var normalized = NormalizeRelativePath(relativePath);
            if (!entries.TryAdd(normalized, new ChecksumEntry(parts[0], normalized, fullPath)))
            {
                throw new InvalidOperationException($"Duplicate checksum entry '{normalized}'.");
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("Hotfix checksum file is empty.");
        }

        return entries;
    }

    private static void RequireChecksum(
        IReadOnlyDictionary<string, ChecksumEntry> checksums,
        string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        if (!checksums.ContainsKey(normalized))
        {
            throw new InvalidOperationException($"Hotfix checksum file is missing '{normalized}'.");
        }
    }

    private static bool IsUnderDirectory(string directory, string path)
    {
        var root = Path.GetFullPath(directory);
        var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, PathComparison);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return NormalizeSeparators(relativePath).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string NormalizeSeparators(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsRootedWithAnySeparator(string path)
    {
        return path.StartsWith(Path.DirectorySeparatorChar)
            || path.StartsWith($"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && IsAnySeparator(path[2]);
    }

    private static bool IsAnySeparator(char value)
    {
        return value is '/' or '\\';
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private sealed record ChecksumEntry(string Hash, string RelativePath, string FullPath);
}
```

Remove the duplicate private checksum methods from
`HotfixPackageInstaller.cs`; the verifier above is the single checksum parser
for both hotfix install and server package validation.

Update `HotfixPackageInstaller.InstallAsync` to call:

```csharp
await HotfixPackageVerifier.VerifyChecksumsAsync(
    staging,
    manifest.Assembly,
    cancellationToken).ConfigureAwait(false);
```

After this edit, `HotfixPackageInstaller` must no longer contain a private
checksum verifier duplicate.

- [ ] **Step 4: Implement the server package validator**

Create `src/Lakona.Tool/Server/ServerPackageValidator.cs`:

```csharp
using System.Text.Json;
using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Server;

internal sealed class ServerPackageValidator
{
    public async Task ValidateAsync(
        string appDirectory,
        ServerPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        ArgumentNullException.ThrowIfNull(manifest);

        RejectBuildOutputDirectories(appDirectory);
        RequireFile(appDirectory, manifest.EntryAssembly);
        RequireFile(appDirectory, "lakona-server.json");

        var currentPath = RequireFile(appDirectory, Path.Combine("hotfix", "current.txt"));
        var currentVersion = (await File.ReadAllTextAsync(currentPath, cancellationToken).ConfigureAwait(false)).Trim();
        if (!StringComparer.Ordinal.Equals(currentVersion, manifest.InitialHotfixVersion))
        {
            throw new InvalidOperationException($"Hotfix current.txt points to '{currentVersion}', but server manifest expects '{manifest.InitialHotfixVersion}'.");
        }

        var versionDirectory = Path.Combine(appDirectory, "hotfix", "versions", manifest.InitialHotfixVersion);
        if (!Directory.Exists(versionDirectory))
        {
            throw new InvalidOperationException($"Initial hotfix version directory is missing: {manifest.InitialHotfixVersion}.");
        }

        RequireFile(versionDirectory, "READY");
        var hotfixManifestPath = RequireFile(versionDirectory, "hotfix.json");
        RequireFile(versionDirectory, "checksums.sha256");

        await using var stream = File.OpenRead(hotfixManifestPath);
        var hotfixManifest = await JsonSerializer.DeserializeAsync<HotfixPackageManifest>(
            stream,
            HotfixJson.Options,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Initial hotfix manifest is invalid.");

        if (!StringComparer.Ordinal.Equals(hotfixManifest.BuildTag, manifest.BuildTag))
        {
            throw new InvalidOperationException($"Server BuildTag '{manifest.BuildTag}' does not match initial hotfix BuildTag '{hotfixManifest.BuildTag}'.");
        }

        if (!StringComparer.Ordinal.Equals(hotfixManifest.Version, manifest.InitialHotfixVersion))
        {
            throw new InvalidOperationException($"Initial hotfix manifest version '{hotfixManifest.Version}' does not match server manifest version '{manifest.InitialHotfixVersion}'.");
        }

        await HotfixPackageVerifier.VerifyChecksumsAsync(
            versionDirectory,
            hotfixManifest.Assembly,
            cancellationToken).ConfigureAwait(false);
    }

    private static void RejectBuildOutputDirectories(string appDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(appDirectory, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Server package contains build output directory '{Path.GetRelativePath(appDirectory, directory)}'.");
            }
        }
    }

    private static string RequireFile(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Server package is missing '{relativePath}'.");
        }

        return path;
    }
}
```

- [ ] **Step 5: Run validator tests and verify they pass**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter ServerPackageValidatorTests
```

Expected: pass.

- [ ] **Step 6: Commit checkpoint**

Run only if this implementation session is using commits:

```powershell
git add src/Lakona.Tool/Hotfix/HotfixPackageVerifier.cs src/Lakona.Tool/Hotfix/HotfixPackageInstaller.cs src/Lakona.Tool/Server/ServerPackageValidator.cs tests/Lakona.Tool.Tests/Server/ServerPackageValidatorTests.cs
git commit -m "feat(tool): validate server package layout"
```

## Task 3: Zip Writer From Published App And Hotfix Package

**Files:**

- Create: `src/Lakona.Tool/Server/ServerPackageWriter.cs`
- Create: `tests/Lakona.Tool.Tests/Server/ServerPackageWriterTests.cs`

- [ ] **Step 1: Write failing zip layout tests**

Create `tests/Lakona.Tool.Tests/Server/ServerPackageWriterTests.cs` with these tests and helpers:

```csharp
using System.IO.Compression;
using System.Text.Json;
using Lakona.Tool.Hotfix;
using Lakona.Tool.Server;
using Xunit;

namespace Lakona.Tool.Tests.Server;

public sealed class ServerPackageWriterTests
{
    [Fact]
    public async Task WritePackageFromPublishedAppAsync_creates_rooted_zip_with_installed_hotfix()
    {
        var root = CreateTempRoot();
        try
        {
            var published = await CreatePublishedAppAsync(root);
            var hotfixZip = await CreateHotfixPackageAsync(root, buildTag: "tag");
            var output = Path.Combine(root, "out");

            var zipPath = await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                new ServerPackageWriteRequest(
                    published,
                    hotfixZip,
                    output,
                    "Server.App.dll",
                    "linux-x64",
                    "Release",
                    "v20260623-153045Z",
                    "tag",
                    new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero)),
                TestContext.Current.CancellationToken);

            Assert.Equal(Path.Combine(output, "Server.App-v20260623-153045Z-linux-x64.zip"), zipPath);
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "Server.App.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "appsettings.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lakona-server.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "hotfix/current.txt");
            Assert.Contains(archive.Entries, entry => entry.FullName == "hotfix/versions/v20260623-153045Z/Server.Hotfix.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "hotfix/versions/v20260623-153045Z/hotfix.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "hotfix/versions/v20260623-153045Z/checksums.sha256");
            Assert.Contains(archive.Entries, entry => entry.FullName == "hotfix/versions/v20260623-153045Z/READY");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("reload.signal", StringComparison.Ordinal));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("app/", StringComparison.Ordinal));

            var manifestEntry = archive.GetEntry("lakona-server.json")!;
            await using var manifestStream = manifestEntry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<ServerPackageManifest>(
                manifestStream,
                ServerJson.Options,
                TestContext.Current.CancellationToken);
            Assert.Equal("v20260623-153045Z", manifest?.Version);
            Assert.Equal("linux-x64", manifest?.Runtime);
            Assert.Equal("Release", manifest?.Configuration);
            Assert.True(manifest?.SelfContained);
            Assert.False(manifest?.Trimmed);
            Assert.Equal("Server.App.dll", manifest?.EntryAssembly);
            Assert.Equal("tag", manifest?.BuildTag);
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
            var published = await CreatePublishedAppAsync(root);
            var hotfixZip = await CreateHotfixPackageAsync(root, buildTag: "hotfix-tag");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    new ServerPackageWriteRequest(
                        published,
                        hotfixZip,
                        Path.Combine(root, "out"),
                        "Server.App.dll",
                        "linux-x64",
                        "Release",
                        "v20260623-153045Z",
                        "app-tag",
                        DateTimeOffset.UtcNow),
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
            var published = await CreatePublishedAppAsync(root);
            var hotfixZip = await CreateHotfixPackageAsync(root, buildTag: "tag", version: "v20260623-000000Z");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new ServerPackageWriter().WritePackageFromPublishedAppAsync(
                    new ServerPackageWriteRequest(
                        published,
                        hotfixZip,
                        Path.Combine(root, "out"),
                        "Server.App.dll",
                        "linux-x64",
                        "Release",
                        "v20260623-153045Z",
                        "tag",
                        DateTimeOffset.UtcNow),
                    TestContext.Current.CancellationToken));

            Assert.Contains("Initial hotfix version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LakonaServerPackageWriterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<string> CreatePublishedAppAsync(string root)
    {
        var published = Path.Combine(root, "published");
        Directory.CreateDirectory(published);
        await File.WriteAllTextAsync(Path.Combine(published, "Server.App.dll"), "app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(published, "appsettings.json"), "{}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(published, "Server.App.runtimeconfig.json"), "{}", TestContext.Current.CancellationToken);
        return published;
    }

    private static Task<string> CreateHotfixPackageAsync(string root, string buildTag, string version = "v20260623-153045Z")
    {
        var build = Path.Combine(root, "hotfix-build-" + Guid.NewGuid().ToString("N"));
        var packages = Path.Combine(root, "hotfix-packages");
        Directory.CreateDirectory(build);
        File.WriteAllText(Path.Combine(build, "Server.Hotfix.dll"), "hotfix");
        File.WriteAllText(Path.Combine(build, "Server.Hotfix.deps.json"), "{}");
        return new HotfixPackageWriter().WritePackageAsync(
            build,
            packages,
            "Server.Hotfix",
            "net10.0",
            buildTag,
            version,
            new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 2: Run writer tests and verify they fail**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter ServerPackageWriterTests
```

Expected: fail because `ServerPackageWriter` does not exist.

- [ ] **Step 3: Implement `WritePackageFromPublishedAppAsync`**

Create `src/Lakona.Tool/Server/ServerPackageWriter.cs`. The class must include:

```csharp
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Server;

internal sealed class ServerPackageWriter
{
    private readonly HotfixPackageInstaller hotfixInstaller;
    private readonly ServerPackageValidator validator;

    public ServerPackageWriter(
        HotfixPackageInstaller? hotfixInstaller = null,
        ServerPackageValidator? validator = null)
    {
        this.hotfixInstaller = hotfixInstaller ?? new HotfixPackageInstaller();
        this.validator = validator ?? new ServerPackageValidator();
    }

    // Add WritePackageFromPublishedAppAsync in this class using the ordered
    // implementation steps below.
}
```

The `WritePackageFromPublishedAppAsync` implementation must do these operations
in this order:

1. Validate every string property on `request` with
   `ArgumentException.ThrowIfNullOrWhiteSpace`.
2. Resolve full paths for `PublishedAppDirectory`, `HotfixPackagePath`, and
   `OutputDirectory`.
3. Fail if the published app directory does not exist.
4. Fail if the hotfix package zip does not exist.
5. Create `<output>/.staging/<guid>/app`.
6. Copy every file and subdirectory from the published app directory into the
   staged app directory.
7. Install the hotfix zip into `<staged app>/hotfix` by calling
   `hotfixInstaller.InstallAsync`.
8. Compare the returned installed version to `request.Version`; throw
   `InvalidOperationException` if they differ.
9. Write `<staged app>/hotfix/current.txt` with exactly `request.Version`.
10. Read `<staged app>/hotfix/versions/<version>/hotfix.json` as
    `HotfixPackageManifest`.
11. Write `lakona-server.json` with `SelfContained = true`,
    `Trimmed = false`, `EntryAssembly = request.EntryAssembly`, and
    `BuildTag = request.BuildTag`.
12. Call `validator.ValidateAsync(stagedApp, manifest, cancellationToken)`.
13. Create the final zip path
    `<output>/<entry-name>-<version>-<runtime>.zip`.
14. Create a temporary zip path in the output directory.
15. Zip the contents of the staged app directory.
16. Replace the final zip only after the temporary zip exists.
17. Delete the staging directory in `finally`.

Add private helpers in the same class:

```csharp
private static void CopyDirectory(string source, string target)
private static string GetZipPath(string outputDirectory, string entryAssembly, string version, string runtimeIdentifier)
private static string GetToolVersion()
private static async Task<HotfixPackageManifest> ReadHotfixManifestAsync(string versionDirectory, CancellationToken cancellationToken)
```

`GetToolVersion` must use the same assembly version pattern as
`HotfixPackageWriter.GetToolVersion`.

- [ ] **Step 4: Run writer tests and verify they pass**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter ServerPackageWriterTests
```

Expected: pass.

- [ ] **Step 5: Commit checkpoint**

Run only if this implementation session is using commits:

```powershell
git add src/Lakona.Tool/Server/ServerPackageWriter.cs tests/Lakona.Tool.Tests/Server/ServerPackageWriterTests.cs
git commit -m "feat(tool): write server package zip"
```

## Task 4: Dotnet And Hotfix Builder Abstractions

**Files:**

- Create: `src/Lakona.Tool/Server/DotNetCommandResult.cs`
- Create: `src/Lakona.Tool/Server/IDotNetCommandRunner.cs`
- Create: `src/Lakona.Tool/Server/DotNetCommandRunner.cs`
- Create: `src/Lakona.Tool/Server/IHotfixPackageBuilder.cs`
- Create: `src/Lakona.Tool/Server/HotfixPackageBuilder.cs`
- Create: `src/Lakona.Tool/Server/IServerPackageWriter.cs`
- Modify: `src/Lakona.Tool/Server/ServerPackageWriter.cs`
- Modify: `tests/Lakona.Tool.Tests/Server/ServerPackageWriterTests.cs`

- [ ] **Step 1: Add failing orchestration tests**

Append these tests to `ServerPackageWriterTests`:

```csharp
[Fact]
public async Task PackAsync_runs_self_contained_untrimmed_publish_and_uses_same_configuration_for_hotfix()
{
    var root = CreateTempRoot();
    try
    {
        var project = await CreateProjectFileAsync(root, "Server.App.csproj", "Server.App", "tag");
        var hotfixProject = await CreateProjectFileAsync(root, "Server.Hotfix.csproj", "Server.Hotfix", "tag");
        var hotfixZip = await CreateHotfixPackageAsync(root, buildTag: "tag");
        var runner = new FakeDotNetCommandRunner("Server.App.dll");
        var hotfixBuilder = new FakeHotfixPackageBuilder(hotfixZip);
        var writer = new ServerPackageWriter(runner, hotfixBuilder, new HotfixPackageInstaller(), new ServerPackageValidator(), () => new DateTimeOffset(2026, 6, 23, 15, 30, 45, TimeSpan.Zero));

        var zip = await writer.PackAsync(
            new ServerPackOptions(project, hotfixProject, Path.Combine(root, "out"), "linux-x64", "Debug", "v20260623-153045Z"),
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(zip));
        var publishCall = Assert.Single(runner.Calls);
        Assert.Contains("publish", publishCall.Arguments);
        Assert.Contains(project, publishCall.Arguments);
        Assert.Contains("-c", publishCall.Arguments);
        Assert.Contains("Debug", publishCall.Arguments);
        Assert.Contains("-r", publishCall.Arguments);
        Assert.Contains("linux-x64", publishCall.Arguments);
        Assert.Contains("--self-contained", publishCall.Arguments);
        Assert.Contains("true", publishCall.Arguments);
        Assert.DoesNotContain(publishCall.Arguments, argument => argument.Contains("PublishTrimmed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publishCall.Arguments, argument => argument.Contains("PublishSingleFile", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Debug", hotfixBuilder.Configuration);
        Assert.Equal("v20260623-153045Z", hotfixBuilder.Version);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

[Fact]
public async Task PackAsync_rejects_missing_runtime()
{
    var root = CreateTempRoot();
    try
    {
        var writer = new ServerPackageWriter(new FakeDotNetCommandRunner("Server.App.dll"), new FakeHotfixPackageBuilder("unused.zip"));

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await writer.PackAsync(
                new ServerPackOptions("app.csproj", "hotfix.csproj", root, "", "Release", "v20260623-153045Z"),
                TestContext.Current.CancellationToken));

        Assert.Contains("RuntimeIdentifier", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

[Fact]
public async Task PackAsync_reports_dotnet_publish_failure()
{
    var root = CreateTempRoot();
    try
    {
        var project = await CreateProjectFileAsync(root, "Server.App.csproj", "Server.App", "tag");
        var hotfixProject = await CreateProjectFileAsync(root, "Server.Hotfix.csproj", "Server.Hotfix", "tag");
        var runner = new FakeDotNetCommandRunner("Server.App.dll")
        {
            Result = new DotNetCommandResult(1, "publish out", "publish err")
        };
        var writer = new ServerPackageWriter(runner, new FakeHotfixPackageBuilder("unused.zip"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await writer.PackAsync(
                new ServerPackOptions(project, hotfixProject, Path.Combine(root, "out"), "linux-x64", "Release", "v20260623-153045Z"),
                TestContext.Current.CancellationToken));

        Assert.Contains("dotnet publish failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish out", exception.Message, StringComparison.Ordinal);
        Assert.Contains("publish err", exception.Message, StringComparison.Ordinal);
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}
```

Add these helpers inside `ServerPackageWriterTests`:

```csharp
private static async Task<string> CreateProjectFileAsync(string root, string fileName, string assemblyName, string buildTag)
{
    var projectDirectory = Path.Combine(root, Path.GetFileNameWithoutExtension(fileName));
    Directory.CreateDirectory(projectDirectory);
    await File.WriteAllTextAsync(
        Path.Combine(projectDirectory, fileName),
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <Import Project="BuildTag.props" />
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <AssemblyName>{assemblyName}</AssemblyName>
          </PropertyGroup>
        </Project>
        """,
        TestContext.Current.CancellationToken);
    await File.WriteAllTextAsync(
        Path.Combine(projectDirectory, "BuildTag.props"),
        $"""
        <Project>
          <PropertyGroup>
            <LakonaHotfixBuildTag>{buildTag}</LakonaHotfixBuildTag>
          </PropertyGroup>
        </Project>
        """,
        TestContext.Current.CancellationToken);
    return Path.Combine(projectDirectory, fileName);
}

private sealed class FakeDotNetCommandRunner : IDotNetCommandRunner
{
    private readonly string entryAssembly;

    public FakeDotNetCommandRunner(string entryAssembly)
    {
        this.entryAssembly = entryAssembly;
    }

    public DotNetCommandResult Result { get; set; } = new(0, "", "");
    public List<(string WorkingDirectory, string[] Arguments)> Calls { get; } = [];

    public async Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        Calls.Add((workingDirectory, arguments.ToArray()));
        var outputIndex = arguments.ToList().IndexOf("-o");
        if (Result.ExitCode == 0 && outputIndex >= 0)
        {
            var publishDirectory = arguments[outputIndex + 1];
            Directory.CreateDirectory(publishDirectory);
            await File.WriteAllTextAsync(Path.Combine(publishDirectory, entryAssembly), "app", cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(publishDirectory, "appsettings.json"), "{}", cancellationToken);
        }

        return Result;
    }
}

private sealed class FakeHotfixPackageBuilder : IHotfixPackageBuilder
{
    private readonly string packagePath;

    public FakeHotfixPackageBuilder(string packagePath)
    {
        this.packagePath = packagePath;
    }

    public string? ProjectPath { get; private set; }
    public string? OutputDirectory { get; private set; }
    public string? Configuration { get; private set; }
    public string? Version { get; private set; }

    public Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        CancellationToken cancellationToken)
    {
        ProjectPath = projectPath;
        OutputDirectory = outputDirectory;
        Configuration = configuration;
        Version = version;
        return Task.FromResult(packagePath);
    }
}
```

- [ ] **Step 2: Run orchestration tests and verify they fail**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "ServerPackageWriterTests"
```

Expected: fail because `IDotNetCommandRunner`, `IHotfixPackageBuilder`, and
`PackAsync` orchestration are not implemented.

- [ ] **Step 3: Add process and hotfix builder abstractions**

Create `src/Lakona.Tool/Server/DotNetCommandResult.cs`:

```csharp
namespace Lakona.Tool.Server;

internal sealed record DotNetCommandResult(int ExitCode, string StandardOutput, string StandardError);
```

Create `src/Lakona.Tool/Server/IDotNetCommandRunner.cs`:

```csharp
namespace Lakona.Tool.Server;

internal interface IDotNetCommandRunner
{
    Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
```

Create `src/Lakona.Tool/Server/DotNetCommandRunner.cs`:

```csharp
using System.Diagnostics;

namespace Lakona.Tool.Server;

internal sealed class DotNetCommandRunner : IDotNetCommandRunner
{
    public async Task<DotNetCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new DotNetCommandResult(
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }
}
```

Create `src/Lakona.Tool/Server/IHotfixPackageBuilder.cs`:

```csharp
namespace Lakona.Tool.Server;

internal interface IHotfixPackageBuilder
{
    Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        CancellationToken cancellationToken);
}
```

Create `src/Lakona.Tool/Server/HotfixPackageBuilder.cs`:

```csharp
using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Server;

internal sealed class HotfixPackageBuilder : IHotfixPackageBuilder
{
    public Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        CancellationToken cancellationToken)
    {
        return new HotfixPackageWriter().PackAsync(
            projectPath,
            outputDirectory,
            configuration,
            version,
            cancellationToken);
    }
}
```

Create `src/Lakona.Tool/Server/IServerPackageWriter.cs`:

```csharp
namespace Lakona.Tool.Server;

internal interface IServerPackageWriter
{
    Task<string> PackAsync(ServerPackOptions options, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement `ServerPackageWriter.PackAsync`**

Update `ServerPackageWriter` so the class implements the writer interface:

```csharp
internal sealed class ServerPackageWriter : IServerPackageWriter
```

Then update its fields and constructor to accept dependencies:

```csharp
private readonly IDotNetCommandRunner dotNet;
private readonly IHotfixPackageBuilder hotfixPackageBuilder;
private readonly HotfixPackageInstaller hotfixInstaller;
private readonly ServerPackageValidator validator;
private readonly Func<DateTimeOffset> utcNow;

public ServerPackageWriter(
    IDotNetCommandRunner? dotNet = null,
    IHotfixPackageBuilder? hotfixPackageBuilder = null,
    HotfixPackageInstaller? hotfixInstaller = null,
    ServerPackageValidator? validator = null,
    Func<DateTimeOffset>? utcNow = null)
{
    this.dotNet = dotNet ?? new DotNetCommandRunner();
    this.hotfixPackageBuilder = hotfixPackageBuilder ?? new HotfixPackageBuilder();
    this.hotfixInstaller = hotfixInstaller ?? new HotfixPackageInstaller();
    this.validator = validator ?? new ServerPackageValidator();
    this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
}
```

Use `hotfixInstaller` inside `WritePackageFromPublishedAppAsync` instead of
creating a new installer directly.

Implement `PackAsync` with this sequence:

1. Validate all `ServerPackOptions` string properties.
2. Resolve full paths for project, hotfix project, and output directory.
3. Throw `FileNotFoundException` if project or hotfix project is missing.
4. Read the app assembly name from the app project XML. Use `<AssemblyName>`
   when present; otherwise use the project file name without extension.
5. Read the stable app BuildTag from `BuildTag.props` in the app project
   directory. Throw `InvalidOperationException` when
   `<LakonaHotfixBuildTag>` is missing or empty.
6. Create output staging directory `<output>/.staging/<guid>/publish`.
7. Run `dotnet publish` through `dotNet.RunAsync` with these exact arguments:
   `publish`, `<full project path>`, `-c`, `<configuration>`, `-r`,
   `<runtime>`, `--self-contained`, `true`, `-o`, `<publish directory>`,
   `/nologo`.
8. If publish exit code is non-zero, throw `InvalidOperationException` whose
   message includes `dotnet publish failed`, standard output, and standard
   error.
9. Build hotfix package into `<output>/.staging/<guid>/hotfix-package` by
   calling `hotfixPackageBuilder.PackAsync` with the same configuration and
   version.
10. Call `WritePackageFromPublishedAppAsync` with the publish directory, hotfix
   package path, output directory, assembly file name, runtime, configuration,
   version, app BuildTag, and `utcNow()`.
11. Let `WritePackageFromPublishedAppAsync` and `ServerPackageValidator` fail
    when the hotfix package manifest BuildTag does not match the app BuildTag.
    This is required for custom `--project` and `--hotfix-project` paths; do
    not override the hotfix manifest BuildTag.
12. Delete the publish/package staging root in `finally`.

- [ ] **Step 5: Run all server writer tests and verify they pass**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "ServerPackageWriterTests|ServerPackageValidatorTests|ServerPackageManifestTests"
```

Expected: pass.

- [ ] **Step 6: Commit checkpoint**

Run only if this implementation session is using commits:

```powershell
git add src/Lakona.Tool/Server tests/Lakona.Tool.Tests/Server
git commit -m "feat(tool): orchestrate server package publishing"
```

## Task 5: CLI Command Routing And Option Parsing

**Files:**

- Create: `src/Lakona.Tool/Cli/Commands/Server/ServerCommand.cs`
- Create: `src/Lakona.Tool/Cli/Commands/Server/ServerPackCommand.cs`
- Create: `tests/Lakona.Tool.Tests/Cli/ServerPackCommandTests.cs`
- Modify: `src/Lakona.Tool/Cli/CliApplication.cs`
- Modify: `src/Lakona.Tool/Cli/Text/ToolText.cs`

- [ ] **Step 1: Write failing CLI command tests**

Create `tests/Lakona.Tool.Tests/Cli/ServerPackCommandTests.cs`:

```csharp
using Lakona.Tool.Cli.Commands.Server;
using Lakona.Tool.Server;
using Xunit;

namespace Lakona.Tool.Tests.Cli;

public sealed class ServerPackCommandTests
{
    [Fact]
    public async Task RunAsync_requires_runtime()
    {
        var terminal = new FakeTerminal();
        var command = new ServerPackCommand(terminal, new FakeServerPackageWriter());

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            async () => await command.RunAsync([], TestContext.Current.CancellationToken));

        Assert.Contains("--runtime", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_passes_defaults_and_configuration_to_writer()
    {
        var terminal = new FakeTerminal();
        var writer = new FakeServerPackageWriter();
        var command = new ServerPackCommand(terminal, writer);

        var exitCode = await command.RunAsync(
            ["--runtime", "linux-x64", "--configuration", "Debug"],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.NotNull(writer.Options);
        Assert.Equal("Server/App/Server.App.csproj", writer.Options!.ProjectPath);
        Assert.Equal("Server/Hotfix/Server.Hotfix.csproj", writer.Options.HotfixProjectPath);
        Assert.Equal("artifacts/server", writer.Options.OutputDirectory);
        Assert.Equal("linux-x64", writer.Options.RuntimeIdentifier);
        Assert.Equal("Debug", writer.Options.Configuration);
        Assert.StartsWith("v", writer.Options.Version, StringComparison.Ordinal);
        Assert.Contains(terminal.Output, line => line.Contains("Packed server", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_rejects_unknown_option()
    {
        var command = new ServerPackCommand(new FakeTerminal(), new FakeServerPackageWriter());

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            async () => await command.RunAsync(["--runtime", "linux-x64", "--trim", "true"], TestContext.Current.CancellationToken));

        Assert.Contains("--trim", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerCommand_routes_pack_and_rejects_unknown_subcommand()
    {
        var terminal = new FakeTerminal();
        var writer = new FakeServerPackageWriter();
        var exitCode = await new ServerCommand(terminal, writer).RunAsync(["pack", "--runtime", "win-x64"], TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal("win-x64", writer.Options?.RuntimeIdentifier);

        var exception = await Assert.ThrowsAsync<CliUsageException>(
            async () => await new ServerCommand(terminal, writer).RunAsync(["deploy"], TestContext.Current.CancellationToken));
        Assert.Contains("Unknown server subcommand", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeServerPackageWriter : IServerPackageWriter
    {
        public ServerPackOptions? Options { get; private set; }

        public Task<string> PackAsync(ServerPackOptions options, CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.FromResult(Path.Combine(options.OutputDirectory, $"Server.App-{options.Version}-{options.RuntimeIdentifier}.zip"));
        }
    }

    private sealed class FakeTerminal : ICliTerminal
    {
        public bool IsInputRedirected => true;
        public bool IsOutputRedirected => false;
        public List<string> Output { get; } = [];
        public List<string> Errors { get; } = [];
        public string? ReadLine() => null;
        public void Write(string value) => Output.Add(value);
        public void WriteLine(string value) => Output.Add(value);
        public void WriteErrorLine(string value) => Errors.Add(value);
    }
}
```

- [ ] **Step 2: Run CLI command tests and verify they fail**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter ServerPackCommandTests
```

Expected: fail because server command classes do not exist.

- [ ] **Step 3: Implement `ServerCommand`**

Create `src/Lakona.Tool/Cli/Commands/Server/ServerCommand.cs`:

```csharp
using Lakona.Tool.Server;

namespace Lakona.Tool.Cli.Commands.Server;

internal sealed class ServerCommand
{
    private readonly ICliTerminal terminal;
    private readonly IServerPackageWriter writer;

    public ServerCommand(ICliTerminal terminal, IServerPackageWriter? writer = null)
    {
        this.terminal = terminal;
        this.writer = writer ?? new ServerPackageWriter();
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            throw new CliUsageException("Missing server subcommand.");
        }

        return args[0] switch
        {
            "pack" => await new ServerPackCommand(terminal, writer).RunAsync(args.Skip(1).ToArray(), cancellationToken).ConfigureAwait(false),
            _ => throw new CliUsageException($"Unknown server subcommand '{args[0]}'.")
        };
    }
}
```

- [ ] **Step 4: Implement `ServerPackCommand`**

Create `src/Lakona.Tool/Cli/Commands/Server/ServerPackCommand.cs`:

```csharp
using Lakona.Tool.Server;

namespace Lakona.Tool.Cli.Commands.Server;

internal sealed class ServerPackCommand
{
    private readonly ICliTerminal terminal;
    private readonly IServerPackageWriter writer;

    public ServerPackCommand(ICliTerminal terminal, IServerPackageWriter? writer = null)
    {
        this.terminal = terminal;
        this.writer = writer ?? new ServerPackageWriter();
    }

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var project = "Server/App/Server.App.csproj";
        var hotfixProject = "Server/Hotfix/Server.Hotfix.csproj";
        var output = "artifacts/server";
        var configuration = "Release";
        string? runtime = null;
        string? version = null;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            string ReadValue()
            {
                if (index + 1 >= args.Length)
                {
                    throw new CliUsageException($"Missing value for {option}.");
                }

                index++;
                return args[index];
            }

            switch (option)
            {
                case "--project":
                    project = ReadValue();
                    break;
                case "--hotfix-project":
                    hotfixProject = ReadValue();
                    break;
                case "--output":
                    output = ReadValue();
                    break;
                case "--configuration":
                    configuration = ReadValue();
                    break;
                case "--runtime":
                    runtime = ReadValue();
                    break;
                case "--version":
                    version = ReadValue();
                    break;
                default:
                    throw new CliUsageException($"Unsupported server pack option '{option}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(runtime))
        {
            throw new CliUsageException("Missing required option --runtime.");
        }

        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new CliUsageException("Missing value for --configuration.");
        }

        version ??= "v" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'");
        var zipPath = await writer.PackAsync(
            new ServerPackOptions(project, hotfixProject, output, runtime, configuration, version),
            cancellationToken).ConfigureAwait(false);
        terminal.WriteLine($"Packed server {zipPath}.");
        return 0;
    }
}
```

- [ ] **Step 5: Route `server` from `CliApplication`**

Modify `src/Lakona.Tool/Cli/CliApplication.cs`:

```csharp
using Lakona.Tool.Cli.Commands.Server;
```

Add this switch arm in `RunAsync`:

```csharp
"server" => await new ServerCommand(terminal).RunAsync(args.Skip(1).ToArray(), CancellationToken.None).ConfigureAwait(false),
```

- [ ] **Step 6: Update CLI help text**

Modify all three `ToolText.HelpText` language branches to include `server pack`.
The English branch must include this command text:

```txt
lakona-tool server pack --runtime linux-x64 [--configuration Release] [--output artifacts/server]
    Package a self-contained server zip with an installed initial hotfix version.
```

The Simplified Chinese branch must include this command text:

```txt
lakona-tool server pack --runtime linux-x64 [--configuration Release] [--output artifacts/server]
    打包自包含服务端 zip，并内置初始热更版本。
```

The Traditional Chinese branch must include this command text:

```txt
lakona-tool server pack --runtime linux-x64 [--configuration Release] [--output artifacts/server]
    打包自包含伺服器 zip，並內建初始熱更版本。
```

- [ ] **Step 7: Run CLI tests and verify they pass**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter ServerPackCommandTests
```

Expected: pass.

- [ ] **Step 8: Commit checkpoint**

Run only if this implementation session is using commits:

```powershell
git add src/Lakona.Tool/Cli/Commands/Server src/Lakona.Tool/Cli/CliApplication.cs src/Lakona.Tool/Cli/Text/ToolText.cs tests/Lakona.Tool.Tests/Cli/ServerPackCommandTests.cs
git commit -m "feat(tool): add server pack cli command"
```

## Task 6: Documentation And Generated Project Guide

**Files:**

- Modify: `src/Lakona.Tool/README.md`
- Modify: `src/Lakona.Tool/Rendering/Docs/GeneratedProjectGuideRenderer.cs`
- Modify: `docs/tool/generation-architecture.md`
- Modify: `docs/tool/server-pack-command.md`
- Modify: `tests/Lakona.Tool.Tests/Rendering/GeneratedProjectGuideRendererTests.cs`
- Modify: `tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs`

- [ ] **Step 1: Write failing documentation tests**

Append this test to `GeneratedProjectGuideRendererTests`:

```csharp
[Fact]
public void Readme_DistinguishesInitialServerPackageFromHotfixPackage()
{
    var spec = Spec(ClientEngine.Console, TransportKind.Kcp, SerializerKind.MemoryPack,
        DeploymentProfile.None);
    var builder = new GenerationPlanBuilder("Root");

    new GeneratedProjectGuideRenderer().AddFiles(spec, builder);

    var plan = builder.Build();
    var readme = Assert.Single(plan.Files, file => file.RelativePath == "README.md");
    Assert.Contains("lakona-tool server pack --runtime linux-x64", readme.Content, StringComparison.Ordinal);
    Assert.Contains("initial deployable server zip", readme.Content, StringComparison.Ordinal);
    Assert.Contains("lakona-tool hotfix pack", readme.Content, StringComparison.Ordinal);
    Assert.Contains("future hotfix zips", readme.Content, StringComparison.Ordinal);
}
```

Append this test to `ToolArchitectureScanTests`:

```csharp
[Fact]
public void ToolDocs_DescribeServerPackAndHotfixPackSeparately()
{
    var repositoryRoot = FindRepositoryRoot();
    var toolReadme = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Lakona.Tool", "README.md"));
    var architecture = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "tool", "generation-architecture.md"));
    var design = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "tool", "server-pack-command.md"));

    Assert.Contains("lakona-tool server pack --runtime linux-x64", toolReadme, StringComparison.Ordinal);
    Assert.Contains("lakona-tool hotfix pack", toolReadme, StringComparison.Ordinal);
    Assert.Contains("lakona-tool server pack --runtime linux-x64", architecture, StringComparison.Ordinal);
    Assert.Contains("lakona-tool hotfix pack", architecture, StringComparison.Ordinal);
    Assert.Contains("Publish trimming", design, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run documentation tests and verify they fail**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "Readme_DistinguishesInitialServerPackageFromHotfixPackage|ToolDocs_DescribeServerPackAndHotfixPackSeparately"
```

Expected: fail because docs and generated guide do not mention `server pack`.

- [ ] **Step 3: Update `src/Lakona.Tool/README.md`**

Add a section before `## Hotfix Operations`:

````markdown
## Server Package

Create the initial deployable server zip:

```bash
lakona-tool server pack --runtime linux-x64
```

The server package is self-contained, RID-specific, untrimmed, and includes an
installed initial hotfix version under `hotfix/versions/<version>/`.

Use `--configuration Debug` for symbol-rich staging packages:

```bash
lakona-tool server pack --runtime linux-x64 --configuration Debug
```
````

Keep the existing `## Hotfix Operations` section and make sure it still says
`lakona-tool hotfix pack` is for later hotfix packages.

- [ ] **Step 4: Update generated project README text**

Modify the `## Tooling` block in
`src/Lakona.Tool/Rendering/Docs/GeneratedProjectGuideRenderer.cs` so it says:

````markdown
Create the initial deployable server zip:

```powershell
lakona-tool server pack --runtime linux-x64
```

Create future hotfix zips after the initial server package has shipped:

```powershell
lakona-tool hotfix pack
```
````

Preserve the compose note that is conditionally rendered for
`DeploymentProfile.Compose`.

- [ ] **Step 5: Update `docs/tool/generation-architecture.md`**

In the Hotfix Operations area, add a short Server Package subsection before the
hotfix command list:

```markdown
## Server Package Operation

`lakona-tool server pack --runtime linux-x64` creates the initial deployable
server zip. It publishes `Server/App/Server.App.csproj` as a self-contained,
RID-specific, untrimmed application and installs the initial hotfix version into
the production `hotfix/current.txt` plus `hotfix/versions/<version>/READY`
layout.

`lakona-tool hotfix pack` remains the follow-up patch package command.
```

Keep the existing v1 statement that Lakona.Tool does not own remote deployment
or multi-node orchestration.

- [ ] **Step 6: Update `docs/tool/server-pack-command.md`**

If implementation details changed while coding, update this design document so
it matches the implemented command. The document must still state:

- `--runtime` is required.
- `--configuration` defaults to `Release`.
- self-contained is always true in v1.
- trim is not supported in v1.
- Docker is not supported in v1.

- [ ] **Step 7: Run documentation tests and verify they pass**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj --filter "GeneratedProjectGuideRendererTests|ToolDocs_DescribeServerPackAndHotfixPackSeparately"
```

Expected: pass.

- [ ] **Step 8: Commit checkpoint**

Run only if this implementation session is using commits:

```powershell
git add src/Lakona.Tool/README.md src/Lakona.Tool/Rendering/Docs/GeneratedProjectGuideRenderer.cs docs/tool/generation-architecture.md docs/tool/server-pack-command.md tests/Lakona.Tool.Tests/Rendering/GeneratedProjectGuideRendererTests.cs tests/Lakona.Tool.Tests/Integration/ToolArchitectureScanTests.cs
git commit -m "docs(tool): document server pack command"
```

## Task 7: Version Bump

**Files:**

- Modify: `src/Lakona.Tool/Lakona.Tool.csproj`

- [ ] **Step 1: Change the Lakona.Tool package version**

In `src/Lakona.Tool/Lakona.Tool.csproj`, change:

```xml
<Version>0.13.0</Version>
```

to:

```xml
<Version>0.14.0</Version>
```

This is required because the implementation changes shippable package content
under `src/Lakona.Tool`.

- [ ] **Step 2: Verify the version text**

Run:

```powershell
rg -n "<Version>0.14.0</Version>" src/Lakona.Tool/Lakona.Tool.csproj
```

Expected: one match.

- [ ] **Step 3: Commit checkpoint**

Run only if this implementation session is using commits:

```powershell
git add src/Lakona.Tool/Lakona.Tool.csproj
git commit -m "chore(tool): bump package version for server pack"
```

## Task 8: Full Verification

**Files:**

- No planned source edits.

- [ ] **Step 1: Run targeted Lakona.Tool tests**

Run:

```powershell
dotnet test tests/Lakona.Tool.Tests/Lakona.Tool.Tests.csproj
```

Expected: exit code 0.

- [ ] **Step 2: Build the tool project**

Run:

```powershell
dotnet build src/Lakona.Tool/Lakona.Tool.csproj
```

Expected: exit code 0.

- [ ] **Step 3: Run repository-level validation unless the user explicitly skips it**

Run:

```powershell
dotnet build Lakona.slnx
dotnet test Lakona.slnx --no-build
```

Expected: exit code 0 for both commands. If the full solution test run times
out, follow the sequential test-project loop from `CONTRIBUTING.md` and record
which project fails first.

- [ ] **Step 4: Inspect changed files**

Run:

```powershell
git status --short
git diff --stat
```

Expected changed areas:

- `src/Lakona.Tool/**`
- `tests/Lakona.Tool.Tests/**`
- `docs/tool/**`

Unexpected changed areas must be explained before handoff.

- [ ] **Step 5: Final source scan**

Run:

```powershell
rg -n "PublishTrimmed|--trim|PublishSingleFile|NativeAOT|--self-contained" src/Lakona.Tool tests/Lakona.Tool.Tests docs/tool
```

Expected:

- No `PublishTrimmed` in implementation code.
- No `PublishSingleFile` in implementation code.
- No `NativeAOT` in implementation code.
- `--self-contained` appears only in publish argument construction or docs.
- `--trim` appears only in docs explaining that v1 does not expose it and in
  `ServerPackCommandTests` where it verifies the option is rejected.

- [ ] **Step 6: Commit final checkpoint**

Run only if this implementation session is using commits and prior tasks were
not committed individually:

```powershell
git add src/Lakona.Tool tests/Lakona.Tool.Tests docs/tool
git commit -m "feat(tool): add server pack command"
```

## Handoff Checklist

Before asking for review, confirm every item:

- [ ] `lakona-tool server pack --runtime linux-x64` routes through
  `CliApplication`.
- [ ] `--runtime` is required.
- [ ] `--configuration Debug` reaches both `dotnet publish` and hotfix package
  build.
- [ ] `dotnet publish` uses `--self-contained true`.
- [ ] The implementation exposes no `--trim` option.
- [ ] The implementation passes no trimming property to `dotnet publish`.
- [ ] The final zip root contains `Server.App.dll` directly.
- [ ] The final zip contains `lakona-server.json`.
- [ ] The final zip contains `hotfix/current.txt`.
- [ ] The final zip contains `hotfix/versions/<version>/READY`.
- [ ] The final zip does not contain `reload.signal`.
- [ ] Server manifest BuildTag equals initial hotfix manifest BuildTag.
- [ ] `src/Lakona.Tool/Lakona.Tool.csproj` version is `0.14.0`.
- [ ] Tool README distinguishes initial server package from future hotfix
  packages.
- [ ] Generated project README distinguishes initial server package from future
  hotfix packages.
