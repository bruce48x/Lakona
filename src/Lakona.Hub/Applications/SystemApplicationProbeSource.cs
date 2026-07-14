using System.Diagnostics;
using Microsoft.Win32;

namespace Lakona.Hub.Applications;

internal sealed class SystemApplicationProbeSource : IApplicationProbeSource
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public IEnumerable<LocalApplicationInstallation> FindApplications()
    {
        var candidates = new List<(LocalApplicationKind Kind, string Path)>();
        AddKnownPaths(candidates);
        AddPathCandidates(candidates);
        AddRegistryCandidates(candidates);

        foreach (var (kind, path) in candidates)
        {
            if (!TryNormalizeExecutable(kind, path, out var executablePath))
            {
                continue;
            }

            yield return new LocalApplicationInstallation(
                kind,
                DisplayName(kind),
                executablePath,
                ReadVersion(kind, executablePath));
        }
    }

    private static void AddKnownPaths(ICollection<(LocalApplicationKind, string)> candidates)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        AddFile(candidates, LocalApplicationKind.Rider, Path.Combine(localAppData, "Programs", "Rider", "bin", "rider64.exe"));
        AddFiles(candidates, LocalApplicationKind.Rider, Path.Combine(localAppData, "JetBrains", "Toolbox", "apps", "Rider"), "rider64.exe");
        AddFiles(candidates, LocalApplicationKind.Rider, Path.Combine(programFiles, "JetBrains"), "rider64.exe");

        AddFiles(candidates, LocalApplicationKind.VisualStudio, Path.Combine(programFiles, "Microsoft Visual Studio"), "devenv.exe");
        AddFiles(candidates, LocalApplicationKind.VisualStudio, Path.Combine(programFilesX86, "Microsoft Visual Studio"), "devenv.exe");

        AddFile(candidates, LocalApplicationKind.VisualStudioCode, Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"));
        AddFile(candidates, LocalApplicationKind.VisualStudioCode, Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"));
        AddFile(candidates, LocalApplicationKind.VisualStudioCode, Path.Combine(programFilesX86, "Microsoft VS Code", "Code.exe"));

        AddFiles(candidates, LocalApplicationKind.Unity, Path.Combine(programFiles, "Unity", "Hub", "Editor"), "Unity.exe");
        AddFile(candidates, LocalApplicationKind.Unity, Path.Combine(programFiles, "Unity", "Editor", "Unity.exe"));

        AddFiles(candidates, LocalApplicationKind.Godot, Path.Combine(localAppData, "Programs", "Godot"), "Godot*.exe");
        AddFiles(candidates, LocalApplicationKind.Godot, Path.Combine(programFiles, "Godot"), "Godot*.exe");
        AddPortableGodotInstallations(candidates);

        AddEnvironmentHome(candidates, LocalApplicationKind.Rider, "RIDER_HOME", "bin", "rider64.exe");
        AddEnvironmentHome(candidates, LocalApplicationKind.Unity, "UNITY_HOME", "Editor", "Unity.exe");
        AddEnvironmentHome(candidates, LocalApplicationKind.Godot, "GODOT_HOME", "Godot.exe");
    }

    private static void AddPortableGodotInstallations(ICollection<(LocalApplicationKind, string)> candidates)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(drive =>
                         drive.IsReady && drive.DriveType == DriveType.Fixed))
            {
                foreach (var root in Directory.EnumerateDirectories(
                             drive.RootDirectory.FullName,
                             "Godot*",
                             SearchOption.TopDirectoryOnly))
                {
                    AddFiles(candidates, LocalApplicationKind.Godot, root, "Godot*.exe");
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Portable installations are optional; registered and PATH installs remain available.
        }
    }

    private static void AddEnvironmentHome(
        ICollection<(LocalApplicationKind, string)> candidates,
        LocalApplicationKind kind,
        string variable,
        params string[] relativePath)
    {
        if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } home)
        {
            AddFile(candidates, kind, Path.Combine([home, .. relativePath]));
        }
    }

    private static void AddPathCandidates(ICollection<(LocalApplicationKind, string)> candidates)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddFile(candidates, LocalApplicationKind.Rider, Path.Combine(directory, "rider64.exe"));
            AddFile(candidates, LocalApplicationKind.Rider, Path.Combine(directory, "rider.exe"));
            AddFile(candidates, LocalApplicationKind.VisualStudio, Path.Combine(directory, "devenv.exe"));
            AddFile(candidates, LocalApplicationKind.VisualStudioCode, Path.Combine(directory, "Code.exe"));
            AddFile(candidates, LocalApplicationKind.Unity, Path.Combine(directory, "Unity.exe"));
            AddFiles(candidates, LocalApplicationKind.Godot, directory, "Godot*.exe", recursive: false);
        }
    }

    private static void AddRegistryCandidates(ICollection<(LocalApplicationKind, string)> candidates)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var root in UninstallRoots)
                    {
                        using var uninstall = baseKey.OpenSubKey(root);
                        if (uninstall is null)
                        {
                            continue;
                        }

                        foreach (var subKeyName in uninstall.GetSubKeyNames())
                        {
                            using var entry = uninstall.OpenSubKey(subKeyName);
                            var displayName = entry?.GetValue("DisplayName") as string;
                            if (!TryClassifyDisplayName(displayName, out var kind))
                            {
                                continue;
                            }

                            if (entry?.GetValue("DisplayIcon") is string displayIcon)
                            {
                                AddFile(candidates, kind, ParseDisplayIcon(displayIcon));
                            }

                            if (entry?.GetValue("InstallLocation") is string installLocation)
                            {
                                AddInstallLocationCandidates(candidates, kind, installLocation);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    // Registry inspection is best-effort. Known paths and PATH remain available.
                }
            }
        }
    }

    private static void AddInstallLocationCandidates(
        ICollection<(LocalApplicationKind, string)> candidates,
        LocalApplicationKind kind,
        string installLocation)
    {
        var relativeCandidates = kind switch
        {
            LocalApplicationKind.Rider => new[] { "bin/rider64.exe", "bin/rider.exe" },
            LocalApplicationKind.VisualStudio => new[] { "Common7/IDE/devenv.exe", "devenv.exe" },
            LocalApplicationKind.VisualStudioCode => new[] { "Code.exe" },
            LocalApplicationKind.Unity => new[] { "Editor/Unity.exe", "Unity.exe" },
            LocalApplicationKind.Godot => new[] { "Godot.exe" },
            _ => []
        };

        foreach (var relativePath in relativeCandidates)
        {
            AddFile(candidates, kind, Path.Combine(installLocation, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        if (kind == LocalApplicationKind.Godot)
        {
            AddFiles(candidates, kind, installLocation, "Godot*.exe", recursive: false);
        }
    }

    private static bool TryClassifyDisplayName(string? displayName, out LocalApplicationKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        if (displayName.Contains("Rider", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.Rider;
            return true;
        }

        if (displayName.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.VisualStudioCode;
            return true;
        }

        if (displayName.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.VisualStudio;
            return true;
        }

        if (displayName.Contains("Unity Editor", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.Unity;
            return true;
        }

        if (displayName.Contains("Godot", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.Godot;
            return true;
        }

        return false;
    }

    private static string ParseDisplayIcon(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            return closingQuote > 1 ? trimmed[1..closingQuote] : trimmed.Trim('"');
        }

        var comma = trimmed.LastIndexOf(',');
        return comma > 0 ? trimmed[..comma].Trim() : trimmed;
    }

    private static bool TryNormalizeExecutable(
        LocalApplicationKind kind,
        string path,
        out string executablePath)
    {
        executablePath = string.Empty;
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) || !MatchesExecutable(kind, Path.GetFileName(fullPath)))
            {
                return false;
            }

            executablePath = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool MatchesExecutable(LocalApplicationKind kind, string fileName)
    {
        return kind switch
        {
            LocalApplicationKind.Rider => fileName.Equals("rider64.exe", StringComparison.OrdinalIgnoreCase) ||
                                          fileName.Equals("rider.exe", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.VisualStudio => fileName.Equals("devenv.exe", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.VisualStudioCode => fileName.Equals("Code.exe", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.Unity => fileName.Equals("Unity.exe", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.Godot => fileName.StartsWith("Godot", StringComparison.OrdinalIgnoreCase) &&
                                          fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string DisplayName(LocalApplicationKind kind) => kind switch
    {
        LocalApplicationKind.Rider => "Rider",
        LocalApplicationKind.VisualStudio => "Visual Studio",
        LocalApplicationKind.VisualStudioCode => "VS Code",
        LocalApplicationKind.Unity => "Unity",
        LocalApplicationKind.Godot => "Godot",
        _ => kind.ToString()
    };

    private static string? ReadVersion(LocalApplicationKind kind, string executablePath)
    {
        if (kind == LocalApplicationKind.Unity)
        {
            var editorDirectory = Directory.GetParent(executablePath)?.Parent;
            if (editorDirectory?.Parent?.Name.Equals("Editor", StringComparison.OrdinalIgnoreCase) == true)
            {
                return editorDirectory.Name;
            }
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(executablePath).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static void AddFile(
        ICollection<(LocalApplicationKind, string)> candidates,
        LocalApplicationKind kind,
        string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            candidates.Add((kind, path));
        }
    }

    private static void AddFiles(
        ICollection<(LocalApplicationKind, string)> candidates,
        LocalApplicationKind kind,
        string root,
        string pattern,
        bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(
                         root,
                         pattern,
                         recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                candidates.Add((kind, path));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // One inaccessible installation root must not hide other applications.
        }
    }
}
