namespace Lakona.Hub;

internal static class HubRuntimeInfo
{
    public const string RequiredSdkVersion = "10.0.100";

    public static bool IsCompatibleSdkVersion(Version version) => version.Major == 10;

    public static string Platform()
    {
        var os = OperatingSystem.IsWindows()
            ? "win"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "osx"
                    : throw new PlatformNotSupportedException("Lakona Hub SDK installation is unsupported on this platform.");
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            var value => throw new PlatformNotSupportedException($"Lakona Hub SDK installation is unsupported on {value}.")
        };
        return $"{os}-{architecture}";
    }

    public static string ManagedSdkRoot()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        root = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lakona", "hub")
            : Path.Combine(root, "Lakona", "Hub");
        return Path.Combine(root, "sdks");
    }

    public static IReadOnlyList<string> SystemDotNetCandidates(string platform)
    {
        var candidates = new List<string> { "dotnet" };
        if (platform.StartsWith("win-", StringComparison.Ordinal))
        {
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet",
                "dotnet.exe"));
        }
        else if (platform.StartsWith("osx-", StringComparison.Ordinal))
        {
            candidates.AddRange([
                "/usr/local/share/dotnet/dotnet",
                "/usr/local/bin/dotnet",
                "/opt/homebrew/bin/dotnet"
            ]);
        }
        else if (platform.StartsWith("linux-", StringComparison.Ordinal))
        {
            candidates.AddRange([
                "/usr/bin/dotnet",
                "/usr/share/dotnet/dotnet",
                "/usr/local/share/dotnet/dotnet",
                "/snap/bin/dotnet"
            ]);
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
