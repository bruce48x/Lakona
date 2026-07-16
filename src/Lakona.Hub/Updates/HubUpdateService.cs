using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lakona.Hub.Updates;

internal interface IHubUpdateService
{
    string CurrentVersion { get; }

    Task<HubAvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default);

    Task PrepareAndLaunchAsync(HubAvailableUpdate update, CancellationToken cancellationToken = default);
}

internal sealed record HubAvailableUpdate(
    string Version,
    string Platform,
    string Tag,
    HubReleaseAsset Asset);

internal sealed class HubUpdateService : IHubUpdateService
{
    private const string Repository = "bruce48x/Lakona";
    private const string ManifestAssetName = "lakona-hub-manifest.json";
    private readonly HttpClient httpClient;
    private readonly string platform;
    private readonly string updateRoot;
    private readonly IHubSystemPackageLauncher systemPackageLauncher;

    public HubUpdateService()
        : this(
            CreateHttpClient(),
            CurrentApplicationVersion(),
            HubPlatform.Current(),
            HubInstallation.UpdateRoot(),
            new HubSystemPackageLauncher())
    {
    }

    internal HubUpdateService(
        HttpClient httpClient,
        string currentVersion,
        string platform,
        string updateRoot,
        IHubSystemPackageLauncher? systemPackageLauncher = null)
    {
        this.httpClient = httpClient;
        CurrentVersion = currentVersion;
        this.platform = platform;
        this.updateRoot = updateRoot;
        this.systemPackageLauncher = systemPackageLauncher ?? new HubSystemPackageLauncher();
    }

    public string CurrentVersion { get; }

    public async Task<HubAvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var releasesResponse = await httpClient.GetAsync(
            $"https://api.github.com/repos/{Repository}/releases?per_page=30",
            cancellationToken);
        releasesResponse.EnsureSuccessStatusCode();
        await using var releaseStream = await releasesResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var releases = await JsonDocument.ParseAsync(releaseStream, cancellationToken: cancellationToken);
        HubAvailableUpdate? latestUpdate = null;

        foreach (var release in releases.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() ||
                release.GetProperty("prerelease").GetBoolean())
            {
                continue;
            }

            var tag = release.GetProperty("tag_name").GetString();
            if (tag is null || !tag.StartsWith("hub-v", StringComparison.Ordinal))
            {
                continue;
            }

            string? manifestUrl = null;
            foreach (var assetElement in release.GetProperty("assets").EnumerateArray())
            {
                if (string.Equals(assetElement.GetProperty("name").GetString(), ManifestAssetName, StringComparison.Ordinal))
                {
                    manifestUrl = assetElement.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
            if (manifestUrl is null)
            {
                continue;
            }

            var manifestJson = await httpClient.GetStringAsync(manifestUrl, cancellationToken);
            var manifest = HubReleaseManifest.Parse(manifestJson);
            if (!string.Equals(manifest.Tag, tag, StringComparison.Ordinal) ||
                !string.Equals(manifest.Repository, Repository, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update manifest does not match its GitHub Release.");
            }

            if (HubVersionComparer.Compare(manifest.Version, CurrentVersion) <= 0)
            {
                continue;
            }

            if (latestUpdate is not null &&
                HubVersionComparer.Compare(manifest.Version, latestUpdate.Version) <= 0)
            {
                continue;
            }

            if (!manifest.Platforms.TryGetValue(platform, out var releasePlatform))
            {
                throw new PlatformNotSupportedException($"Release {manifest.Version} does not support {platform}.");
            }

            latestUpdate = new HubAvailableUpdate(
                manifest.Version,
                platform,
                manifest.Tag,
                releasePlatform.Full);
        }

        return latestUpdate;
    }

    public async Task PrepareAndLaunchAsync(
        HubAvailableUpdate update,
        CancellationToken cancellationToken = default)
    {
        var updateDirectory = Path.Combine(updateRoot, update.Version, update.Platform);
        Directory.CreateDirectory(updateDirectory);
        if (update.Asset.AssetName.Contains('/') || update.Asset.AssetName.Contains('\\') ||
            update.Asset.Sha256.Length != 64 || update.Asset.Size < 1)
        {
            throw new InvalidDataException("The update asset metadata is invalid.");
        }

        var archivePath = Path.Combine(updateDirectory, Path.GetFileName(update.Asset.AssetName));
        var assetUrl = $"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(update.Tag)}/{Uri.EscapeDataString(update.Asset.AssetName)}";

        using (var response = await httpClient.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);
        }

        await VerifyAssetAsync(archivePath, update.Asset, cancellationToken);

        systemPackageLauncher.Open(archivePath);
    }

    private static async Task VerifyAssetAsync(
        string path,
        HubReleaseAsset asset,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length != asset.Size)
        {
            throw new InvalidDataException("The downloaded update size does not match the release manifest.");
        }

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexStringLower(hash);
        if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded update checksum does not match the release manifest.");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Lakona-Hub", CurrentApplicationVersion()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string CurrentApplicationVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return informational?.Split('+', 2)[0]
            ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }
}

internal static class HubPlatform
{
    public static string Current()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            var value => throw new PlatformNotSupportedException($"Unsupported process architecture: {value}.")
        };
        if (OperatingSystem.IsWindows())
        {
            return $"win-{architecture}";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"osx-{architecture}";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"linux-{architecture}-{LinuxPackageFormat.Current()}";
        }

        throw new PlatformNotSupportedException("Lakona Hub updates support Windows, macOS, and Linux.");
    }

}

internal static class LinuxPackageFormat
{
    public static string Current()
    {
        const string osReleasePath = "/etc/os-release";
        var osRelease = File.Exists(osReleasePath) ? File.ReadAllText(osReleasePath) : string.Empty;
        return Detect(osRelease, File.Exists);
    }

    internal static string Detect(string osRelease, Func<string, bool> fileExists)
    {
        var values = osRelease
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => parts[1].Trim().Trim('"', '\''),
                StringComparer.OrdinalIgnoreCase);
        var family = $"{values.GetValueOrDefault("ID")} {values.GetValueOrDefault("ID_LIKE")}";
        var identifiers = family
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToLowerInvariant());
        if (identifiers.Any(IsDebianFamily))
        {
            return "deb";
        }

        if (identifiers.Any(IsRpmFamily))
        {
            return "rpm";
        }

        if (fileExists("/usr/bin/dpkg"))
        {
            return "deb";
        }

        if (fileExists("/usr/bin/rpm"))
        {
            return "rpm";
        }

        throw new PlatformNotSupportedException(
            "Lakona Hub updates require a Debian-family or RPM-family Linux distribution.");
    }

    private static bool IsDebianFamily(string value) => value is
        "debian" or "ubuntu" or "linuxmint" or "pop" or "elementary" or "zorin" or "kali" or "deepin";

    private static bool IsRpmFamily(string value) => value is
        "fedora" or "rhel" or "centos" or "rocky" or "almalinux" or "ol" or "suse" or "opensuse" or "sles";
}

internal interface IHubSystemPackageLauncher
{
    void Open(string packagePath);
}

internal sealed class HubSystemPackageLauncher : IHubSystemPackageLauncher
{
    public void Open(string packagePath)
    {
        ProcessStartInfo startInfo;
        if (OperatingSystem.IsWindows())
        {
            startInfo = new ProcessStartInfo(packagePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(packagePath) ?? Environment.CurrentDirectory
            };
        }
        else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            startInfo = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(packagePath) ?? Environment.CurrentDirectory
            };
            startInfo.ArgumentList.Add(packagePath);
        }
        else
        {
            throw new PlatformNotSupportedException("System package installation is supported only on Windows, macOS, and Linux.");
        }

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not open the system package installer.");
    }
}

internal static class HubVersionComparer
{
    public static bool Equals(string left, string right) => Compare(left, right) == 0;

    public static int Compare(string left, string right)
    {
        var leftVersion = Parse(left);
        var rightVersion = Parse(right);
        var versionComparison = leftVersion.Version.CompareTo(rightVersion.Version);
        if (versionComparison != 0)
        {
            return versionComparison;
        }

        if (leftVersion.PreRelease is null)
        {
            return rightVersion.PreRelease is null ? 0 : 1;
        }

        return rightVersion.PreRelease is null
            ? -1
            : string.Compare(leftVersion.PreRelease, rightVersion.PreRelease, StringComparison.OrdinalIgnoreCase);
    }

    private static (Version Version, string? PreRelease) Parse(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var parts = normalized.Split('-', 2);
        if (!Version.TryParse(parts[0], out var version))
        {
            throw new InvalidDataException($"Invalid Hub version: {value}.");
        }

        return (version, parts.Length == 2 ? parts[1] : null);
    }
}

internal static class HubInstallation
{
    public static string UpdateRoot()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localData, "Lakona", "Hub", "updates");
    }
}
