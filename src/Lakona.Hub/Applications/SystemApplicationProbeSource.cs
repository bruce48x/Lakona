using System.Diagnostics;
using System.Text.Json;
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
            if (!TryCreateInstallation(kind, path, out var installation))
            {
                continue;
            }

            yield return installation;
        }
    }

    internal static bool TryCreateInstallation(
        LocalApplicationKind kind,
        string path,
        out LocalApplicationInstallation installation)
    {
        installation = null!;
        if (!TryNormalizeExecutable(kind, path, out var executablePath))
        {
            return false;
        }

        installation = new LocalApplicationInstallation(
            kind,
            kind == LocalApplicationKind.Other
                ? ReadDisplayName(executablePath)
                : LocalApplicationKinds.DisplayName(kind),
            executablePath,
            ReadVersion(kind, executablePath));
        return true;
    }

    internal static bool TryCreateManualInstallation(
        string path,
        out LocalApplicationInstallation installation)
    {
        foreach (var kind in LocalApplicationKinds.AutomaticallyDetectedKinds)
        {
            if (TryCreateInstallation(kind, path, out installation))
            {
                return true;
            }
        }

        return TryCreateInstallation(LocalApplicationKind.Other, path, out installation);
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

        AddFile(candidates, LocalApplicationKind.UnityHub, Path.Combine(localAppData, "Programs", "Unity Hub", "Unity Hub.exe"));
        AddFile(candidates, LocalApplicationKind.UnityHub, Path.Combine(programFiles, "Unity Hub", "Unity Hub.exe"));
        AddFiles(candidates, LocalApplicationKind.Unity, Path.Combine(programFiles, "Unity", "Hub", "Editor"), "Unity.exe");
        AddFile(candidates, LocalApplicationKind.Unity, Path.Combine(programFiles, "Unity", "Editor", "Unity.exe"));

        AddFile(candidates, LocalApplicationKind.TuanjieHub, Path.Combine(localAppData, "Programs", "Tuanjie Hub", "Tuanjie Hub.exe"));
        AddFile(candidates, LocalApplicationKind.TuanjieHub, Path.Combine(programFiles, "Tuanjie Hub", "Tuanjie Hub.exe"));
        AddVersionedEditorFiles(
            candidates,
            LocalApplicationKind.Tuanjie,
            Path.Combine(programFiles, "Tuanjie", "Hub", "Editor"),
            "Tuanjie.exe");
        AddFile(candidates, LocalApplicationKind.Tuanjie, Path.Combine(programFiles, "Tuanjie", "Editor", "Tuanjie.exe"));

        AddFiles(candidates, LocalApplicationKind.Godot, Path.Combine(localAppData, "Programs", "Godot"), "Godot*.exe");
        AddFiles(candidates, LocalApplicationKind.Godot, Path.Combine(programFiles, "Godot"), "Godot*.exe");
        AddPortableGodotInstallations(candidates);

        AddEnvironmentHome(candidates, LocalApplicationKind.Rider, "RIDER_HOME", "bin", "rider64.exe");
        AddEnvironmentHome(candidates, LocalApplicationKind.Unity, "UNITY_HOME", "Editor", "Unity.exe");
        AddEnvironmentHome(candidates, LocalApplicationKind.Tuanjie, "TUANJIE_HOME", "Editor", "Tuanjie.exe");
        AddEnvironmentHome(candidates, LocalApplicationKind.Godot, "GODOT_HOME", "Godot.exe");

        if (OperatingSystem.IsMacOS())
        {
            AddFile(candidates, LocalApplicationKind.UnityHub, "/Applications/Unity Hub.app");
        }

        if (OperatingSystem.IsLinux())
        {
            AddFile(candidates, LocalApplicationKind.UnityHub, "/opt/unityhub/unityhub");
            AddFile(candidates, LocalApplicationKind.UnityHub, "/usr/bin/unityhub");
        }
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
            AddFile(candidates, LocalApplicationKind.UnityHub, Path.Combine(directory, "Unity Hub.exe"));
            AddFile(candidates, LocalApplicationKind.UnityHub, Path.Combine(directory, "unityhub"));
            AddFile(candidates, LocalApplicationKind.Unity, Path.Combine(directory, "Unity.exe"));
            AddFile(candidates, LocalApplicationKind.TuanjieHub, Path.Combine(directory, "Tuanjie Hub.exe"));
            AddFile(candidates, LocalApplicationKind.Tuanjie, Path.Combine(directory, "Tuanjie.exe"));
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
            LocalApplicationKind.UnityHub => new[] { "Unity Hub.exe", "unityhub" },
            LocalApplicationKind.Unity => new[] { "Editor/Unity.exe", "Unity.exe" },
            LocalApplicationKind.TuanjieHub => new[] { "Tuanjie Hub.exe" },
            LocalApplicationKind.Tuanjie => new[] { "Editor/Tuanjie.exe", "Tuanjie.exe" },
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

        if (displayName.Contains("Unity Hub", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.UnityHub;
            return true;
        }

        if (displayName.Contains("Unity Editor", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.Unity;
            return true;
        }

        if (displayName.Contains("Tuanjie Hub", StringComparison.OrdinalIgnoreCase) ||
            displayName.Contains("团结引擎 Hub", StringComparison.OrdinalIgnoreCase) ||
            displayName.Contains("團結引擎 Hub", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.TuanjieHub;
            return true;
        }

        if (displayName.Contains("Tuanjie", StringComparison.OrdinalIgnoreCase) ||
            displayName.Contains("团结引擎", StringComparison.OrdinalIgnoreCase) ||
            displayName.Contains("團結引擎", StringComparison.OrdinalIgnoreCase))
        {
            kind = LocalApplicationKind.Tuanjie;
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
            if (Directory.Exists(fullPath))
            {
                fullPath = ResolveApplicationBundleExecutable(kind, fullPath) ?? string.Empty;
            }

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

    private static string? ResolveApplicationBundleExecutable(LocalApplicationKind kind, string bundlePath)
    {
        var relativeCandidates = kind switch
        {
            LocalApplicationKind.Rider => new[] { "Contents/MacOS/rider" },
            LocalApplicationKind.VisualStudio => new[] { "Contents/MacOS/VisualStudio" },
            LocalApplicationKind.VisualStudioCode => new[] { "Contents/MacOS/Electron" },
            LocalApplicationKind.UnityHub => new[] { "Contents/MacOS/Unity Hub" },
            LocalApplicationKind.Unity => new[] { "Contents/MacOS/Unity" },
            LocalApplicationKind.TuanjieHub => new[] { "Contents/MacOS/Tuanjie Hub" },
            LocalApplicationKind.Tuanjie => new[] { "Contents/MacOS/Tuanjie" },
            LocalApplicationKind.Godot => new[] { "Contents/MacOS/Godot" },
            _ => []
        };
        return relativeCandidates
            .Select(relative => Path.Combine(bundlePath, relative.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(File.Exists);
    }

    private static bool MatchesExecutable(LocalApplicationKind kind, string fileName)
    {
        return kind switch
        {
            LocalApplicationKind.Rider => fileName.Equals("rider64.exe", StringComparison.OrdinalIgnoreCase) ||
                                          fileName.Equals("rider.exe", StringComparison.OrdinalIgnoreCase) ||
                                          fileName.Equals("rider", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.VisualStudio => fileName.Equals("devenv.exe", StringComparison.OrdinalIgnoreCase) ||
                                                 fileName.Equals("VisualStudio", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.VisualStudioCode => fileName.Equals("Code.exe", StringComparison.OrdinalIgnoreCase) ||
                                                     fileName.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                                                     fileName.Equals("Electron", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.UnityHub => fileName.Equals("Unity Hub.exe", StringComparison.OrdinalIgnoreCase) ||
                                             fileName.Equals("UnityHub.exe", StringComparison.OrdinalIgnoreCase) ||
                                             fileName.Equals("Unity Hub", StringComparison.OrdinalIgnoreCase) ||
                                             fileName.Equals("unityhub", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.Unity => fileName.Equals("Unity.exe", StringComparison.OrdinalIgnoreCase) ||
                                          fileName.Equals("Unity", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.TuanjieHub => fileName.Equals("Tuanjie Hub.exe", StringComparison.OrdinalIgnoreCase) ||
                                               fileName.Equals("Tuanjie Hub", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.Tuanjie => fileName.Equals("Tuanjie.exe", StringComparison.OrdinalIgnoreCase) ||
                                            fileName.Equals("Tuanjie", StringComparison.OrdinalIgnoreCase),
            LocalApplicationKind.Godot => fileName.StartsWith("Godot", StringComparison.OrdinalIgnoreCase) &&
                                           (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                            !Path.HasExtension(fileName)),
            LocalApplicationKind.Other => true,
            _ => false
        };
    }

    private static string? ReadVersion(LocalApplicationKind kind, string executablePath)
    {
        if (kind is LocalApplicationKind.Unity or LocalApplicationKind.Tuanjie)
        {
            var editorDirectory = Directory.GetParent(executablePath)?.Parent;
            if (editorDirectory?.Parent?.Name.Equals("Editor", StringComparison.OrdinalIgnoreCase) == true)
            {
                return kind == LocalApplicationKind.Tuanjie
                    ? ResolveTuanjieVersion(editorDirectory.Name)
                    : editorDirectory.Name;
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

    internal static string ResolveTuanjieVersion(
        string compatibilityVersion,
        string? mappingFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(compatibilityVersion))
        {
            return compatibilityVersion;
        }

        mappingFilePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TuanjieHub",
            "versionMapping.json");
        try
        {
            if (!File.Exists(mappingFilePath))
            {
                return compatibilityVersion;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(mappingFilePath));
            return document.RootElement.TryGetProperty(compatibilityVersion, out var mapped) &&
                   mapped.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(mapped.GetString())
                ? mapped.GetString()!
                : compatibilityVersion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return compatibilityVersion;
        }
    }

    private static string ReadDisplayName(string executablePath)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);
            var name = string.IsNullOrWhiteSpace(versionInfo.ProductName)
                ? versionInfo.FileDescription
                : versionInfo.ProductName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }
        }
        catch (FileNotFoundException)
        {
            // Fall back to the executable name below.
        }

        return Path.GetFileNameWithoutExtension(executablePath);
    }

    private static void AddFile(
        ICollection<(LocalApplicationKind, string)> candidates,
        LocalApplicationKind kind,
        string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
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

    private static void AddVersionedEditorFiles(
        ICollection<(LocalApplicationKind, string)> candidates,
        LocalApplicationKind kind,
        string root,
        string executableName)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        try
        {
            foreach (var versionDirectory in Directory.EnumerateDirectories(root))
            {
                AddFile(candidates, kind, Path.Combine(versionDirectory, "Editor", executableName));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // One inaccessible installation root must not hide other applications.
        }
    }
}
