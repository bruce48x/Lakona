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
    private const string DefaultExecutable = "/usr/bin/flatpak";

    private static string UserRoot => Environment.GetEnvironmentVariable("FLATPAK_USER_DIR") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "flatpak");
    private static string SystemRoot => Environment.GetEnvironmentVariable("FLATPAK_SYSTEM_DIR") ?? "/var/lib/flatpak";

    private static string? FindExecutable()
    {
        if (ApplicationExecutable.IsRunnable(DefaultExecutable))
        {
            return DefaultExecutable;
        }
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Where(Path.IsPathFullyQualified)
            .Select(directory => Path.Combine(directory, "flatpak"))
            .FirstOrDefault(ApplicationExecutable.IsRunnable);
    }

    public static IEnumerable<LocalApplicationInstallation> FindInstallations() =>
        FindInstallations(UserRoot, SystemRoot, FindExecutable() is not null);

    public static FlatpakInstallationScope? GetLauncherScope(string path)
    {
        var resolved = ApplicationExecutable.ResolveLinuxPath(path);
        foreach (var (root, scope) in Roots(UserRoot, SystemRoot))
        {
            var appDirectory = Path.Combine(root, "app", ApplicationId);
            if (IsDeploymentLauncher(path, appDirectory) ||
                IsDeploymentLauncher(resolved, ApplicationExecutable.ResolveLinuxPath(appDirectory)))
            {
                return scope;
            }
        }
        return null;
    }

    private static bool IsDeploymentLauncher(string path, string appDirectory) =>
        path.StartsWith(appDirectory + "/", StringComparison.Ordinal) &&
        (path.EndsWith("/files/bin/rider", StringComparison.Ordinal) || path.EndsWith("/files/bin/rider.sh", StringComparison.Ordinal));

    private static IEnumerable<(string Root, FlatpakInstallationScope Scope)> Roots(string userRoot, string systemRoot)
    {
        if (Path.IsPathFullyQualified(userRoot))
        {
            yield return (userRoot, FlatpakInstallationScope.User);
        }
        if (Path.IsPathFullyQualified(systemRoot))
        {
            yield return (systemRoot, FlatpakInstallationScope.System);
        }
    }

    internal static IEnumerable<LocalApplicationInstallation> FindInstallations(
        string userRoot,
        string systemRoot,
        bool flatpakAvailable)
    {
        if (!flatpakAvailable)
        {
            yield break;
        }

        foreach (var (root, scope) in Roots(userRoot, systemRoot))
        {
            // Flatpak keeps this path stable across revisions of the installed app.
            var files = Path.Combine(root, "app", ApplicationId, "current", "active", "files");
            var executable = Path.Combine(files, "bin", "rider");
            if (ApplicationExecutable.IsRunnable(executable))
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
        return new ApplicationLaunchPlan(FindExecutable() ?? DefaultExecutable, workingDirectory,
            ["run", installation, ApplicationId, .. arguments]);
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
