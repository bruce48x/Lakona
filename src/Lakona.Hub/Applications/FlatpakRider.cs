using System.Text.Json;

namespace Lakona.Hub.Applications;

internal enum FlatpakInstallationScope
{
    User,
    System
}

internal static class FlatpakRider
{
    private const string ApplicationId = "com.jetbrains.Rider";
    private const string FlatpakExecutable = "/usr/bin/flatpak";

    public static IEnumerable<LocalApplicationInstallation> FindInstallations() => FindInstallations(
        Environment.GetEnvironmentVariable("FLATPAK_USER_DIR") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "flatpak"),
        Environment.GetEnvironmentVariable("FLATPAK_SYSTEM_DIR") ?? "/var/lib/flatpak",
        File.Exists(FlatpakExecutable));

    internal static IEnumerable<LocalApplicationInstallation> FindInstallations(
        string userRoot,
        string systemRoot,
        bool flatpakAvailable)
    {
        if (!flatpakAvailable)
        {
            yield break;
        }

        foreach (var (root, scope) in new[]
                 {
                     (userRoot, FlatpakInstallationScope.User),
                     (systemRoot, FlatpakInstallationScope.System)
                 })
        {
            if (!Path.IsPathFullyQualified(root))
            {
                continue;
            }

            // Flatpak keeps this path stable across revisions of the installed app.
            var files = Path.Combine(root, "app", ApplicationId, "current", "active", "files");
            var executable = Path.Combine(files, "bin", "rider");
            if (File.Exists(executable))
            {
                yield return new LocalApplicationInstallation(
                    LocalApplicationKind.Rider,
                    LocalApplicationKinds.DisplayName(LocalApplicationKind.Rider),
                    executable,
                    ReadVersion(Path.Combine(files, "product-info.json")))
                {
                    FlatpakScope = scope
                };
            }
        }
    }

    public static ApplicationLaunchPlan CreateLaunchPlan(
        FlatpakInstallationScope scope,
        string workingDirectory,
        IReadOnlyList<string> arguments)
    {
        var installation = scope switch
        {
            FlatpakInstallationScope.User => "--user",
            FlatpakInstallationScope.System => "--system",
            _ => throw new ArgumentOutOfRangeException(nameof(scope))
        };
        return new ApplicationLaunchPlan(FlatpakExecutable, workingDirectory, ["run", installation, ApplicationId, .. arguments]);
    }

    private static string? ReadVersion(string productInfoPath)
    {
        try
        {
            using var stream = File.OpenRead(productInfoPath);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("version", out var version) &&
                   version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
