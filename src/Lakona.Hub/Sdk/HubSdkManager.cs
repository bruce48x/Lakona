using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lakona.Hub.Sdk;

internal interface IHubSdkManager
{
    Task<HubSdkStatus> InspectAsync(CancellationToken cancellationToken = default);

    Task<HubSdkStatus> InstallAsync(
        IProgress<HubSdkProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

internal enum HubSdkSource
{
    None,
    Managed,
    System
}

internal sealed record HubSdkStatus(
    bool IsReady,
    HubSdkSource Source,
    string? Version,
    string? ExecutablePath);

internal enum HubSdkInstallStage
{
    Resolving,
    Downloading,
    Verifying,
    Extracting,
    Validating,
    Completed
}

internal sealed record HubSdkProgress(
    HubSdkInstallStage Stage,
    long BytesReceived = 0,
    long TotalBytes = 0)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesReceived * 100d / TotalBytes, 0, 100);
}

internal sealed record HubSdkCommandResult(int ExitCode, string Output, string Error);

internal interface IHubSdkCommandRunner
{
    Task<HubSdkCommandResult> RunAsync(
        string executablePath,
        string arguments,
        CancellationToken cancellationToken);
}

internal sealed class HubSdkManager : IHubSdkManager
{
    internal const string ReleaseMetadataUrl =
        "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json";

    private readonly HttpClient httpClient;
    private readonly IHubSdkCommandRunner commandRunner;
    private readonly string platform;
    private readonly string sdkRoot;

    public HubSdkManager()
        : this(CreateHttpClient(), new HubSdkCommandRunner(), HubRuntimeInfo.Platform(), HubRuntimeInfo.ManagedSdkRoot())
    {
    }

    internal HubSdkManager(
        HttpClient httpClient,
        IHubSdkCommandRunner commandRunner,
        string platform,
        string sdkRoot)
    {
        this.httpClient = httpClient;
        this.commandRunner = commandRunner;
        this.platform = platform;
        this.sdkRoot = sdkRoot;
    }

    public async Task<HubSdkStatus> InspectAsync(CancellationToken cancellationToken = default)
    {
        var managedExecutable = ManagedExecutablePath();
        if (File.Exists(managedExecutable))
        {
            var version = await ReadVersionAsync(managedExecutable, cancellationToken);
            if (string.Equals(version, HubRuntimeInfo.RequiredSdkVersion, StringComparison.Ordinal))
            {
                return new HubSdkStatus(true, HubSdkSource.Managed, version, managedExecutable);
            }
        }

        foreach (var executable in HubRuntimeInfo.SystemDotNetCandidates(platform))
        {
            var system = await commandRunner.RunAsync(executable, "--list-sdks", cancellationToken);
            if (system.ExitCode != 0)
            {
                continue;
            }

            var compatibleVersion = system.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split(' ', 2)[0])
                .Select(value => Version.TryParse(value, out var version) ? version : null)
                .OfType<Version>()
                .Where(HubRuntimeInfo.IsCompatibleSdkVersion)
                .OrderDescending()
                .FirstOrDefault();
            if (compatibleVersion is not null)
            {
                return new HubSdkStatus(true, HubSdkSource.System, compatibleVersion.ToString(), executable);
            }
        }

        return new HubSdkStatus(false, HubSdkSource.None, null, null);
    }

    public async Task<HubSdkStatus> InstallAsync(
        IProgress<HubSdkProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new HubSdkProgress(HubSdkInstallStage.Resolving));
        var asset = await ResolveAssetAsync(cancellationToken);
        Directory.CreateDirectory(sdkRoot);
        var downloadRoot = Path.Combine(sdkRoot, ".downloads");
        Directory.CreateDirectory(downloadRoot);
        var archivePath = Path.Combine(downloadRoot, asset.Name + ".part");
        var temporaryInstallPath = Path.Combine(sdkRoot, $".{HubRuntimeInfo.RequiredSdkVersion}-{Guid.NewGuid():N}.tmp");
        var targetPath = ManagedSdkDirectory();

        try
        {
            await DownloadAsync(asset.Url, archivePath, progress, cancellationToken);
            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Verifying));
            await VerifyHashAsync(archivePath, asset.Hash, cancellationToken);

            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Extracting));
            Directory.CreateDirectory(temporaryInstallPath);
            await ExtractAsync(archivePath, temporaryInstallPath, asset.Name, cancellationToken);
            var temporaryExecutable = Path.Combine(
                temporaryInstallPath,
                HostExecutableName());
            if (!File.Exists(temporaryExecutable))
            {
                throw new InvalidDataException("The downloaded SDK archive does not contain the dotnet host.");
            }

            if (!IsWindowsPlatform() && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryExecutable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Validating));
            var version = await ReadVersionAsync(temporaryExecutable, cancellationToken);
            if (!string.Equals(version, HubRuntimeInfo.RequiredSdkVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The downloaded SDK version is invalid. Expected {HubRuntimeInfo.RequiredSdkVersion}, got {version ?? "no version"}.");
            }

            if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)
                                      ?? throw new InvalidOperationException("The managed SDK version directory is unavailable."));
            Directory.Move(temporaryInstallPath, targetPath);
            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Completed));
            return new HubSdkStatus(true, HubSdkSource.Managed, version, ManagedExecutablePath());
        }
        finally
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            if (Directory.Exists(temporaryInstallPath))
            {
                Directory.Delete(temporaryInstallPath, recursive: true);
            }
        }
    }

    private async Task<SdkAsset> ResolveAssetAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(ReleaseMetadataUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        foreach (var release in document.RootElement.GetProperty("releases").EnumerateArray())
        {
            if (!release.TryGetProperty("sdk", out var sdk) ||
                !string.Equals(sdk.GetProperty("version").GetString(), HubRuntimeInfo.RequiredSdkVersion, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var file in sdk.GetProperty("files").EnumerateArray())
            {
                if (string.Equals(file.GetProperty("rid").GetString(), platform, StringComparison.Ordinal))
                {
                    return new SdkAsset(
                        file.GetProperty("name").GetString() ?? throw new InvalidDataException("SDK asset name is missing."),
                        file.GetProperty("url").GetString() ?? throw new InvalidDataException("SDK asset URL is missing."),
                        file.GetProperty("hash").GetString() ?? throw new InvalidDataException("SDK asset hash is missing."));
                }
            }
        }

        throw new InvalidDataException(
            $"The .NET release metadata does not contain SDK {HubRuntimeInfo.RequiredSdkVersion} for {platform}.");
    }

    private async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<HubSdkProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long received = 0;
        progress?.Report(new HubSdkProgress(HubSdkInstallStage.Downloading, received, totalBytes));
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Downloading, received, totalBytes));
        }
    }

    private static async Task VerifyHashAsync(
        string archivePath,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(archivePath);
        var actualHash = Convert.ToHexString(await SHA512.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded SDK failed SHA-512 verification.");
        }
    }

    private static async Task ExtractAsync(
        string archivePath,
        string destinationPath,
        string assetName,
        CancellationToken cancellationToken)
    {
        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, destinationPath, overwriteFiles: true), cancellationToken);
            return;
        }

        if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var archive = File.OpenRead(archivePath);
            await using var gzip = new GZipStream(archive, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gzip, destinationPath, overwriteFiles: true, cancellationToken);
            return;
        }

        throw new InvalidDataException($"Unsupported SDK archive: {assetName}");
    }

    private async Task<string?> ReadVersionAsync(string executablePath, CancellationToken cancellationToken)
    {
        var result = await commandRunner.RunAsync(executablePath, "--version", cancellationToken);
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    private string ManagedSdkDirectory() => Path.Combine(sdkRoot, HubRuntimeInfo.RequiredSdkVersion, platform);

    private string ManagedExecutablePath() => Path.Combine(
        ManagedSdkDirectory(),
        HostExecutableName());

    private string HostExecutableName() => IsWindowsPlatform() ? "dotnet.exe" : "dotnet";

    private bool IsWindowsPlatform() => platform.StartsWith("win-", StringComparison.Ordinal);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lakona-Hub-SdkManager/1.0");
        return client;
    }

    private sealed record SdkAsset(string Name, string Url, string Hash);
}

internal sealed class HubSdkCommandRunner : IHubSdkCommandRunner
{
    public async Task<HubSdkCommandResult> RunAsync(
        string executablePath,
        string arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executablePath)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(arguments);
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new HubSdkCommandResult(-1, "", "Could not start dotnet.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new HubSdkCommandResult(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new HubSdkCommandResult(-1, "", ex.Message);
        }
    }
}
