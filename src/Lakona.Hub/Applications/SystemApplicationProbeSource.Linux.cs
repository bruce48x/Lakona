namespace Lakona.Hub.Applications;

internal sealed partial class SystemApplicationProbeSource
{
    private static readonly Dictionary<string, LocalApplicationKind> LinuxLaunchers = new(StringComparer.Ordinal)
    {
        ["rider"] = LocalApplicationKind.Rider,
        ["rider.sh"] = LocalApplicationKind.Rider,
        ["code"] = LocalApplicationKind.VisualStudioCode,
        ["unityhub"] = LocalApplicationKind.UnityHub,
        ["Unity Hub"] = LocalApplicationKind.UnityHub,
        ["Unity"] = LocalApplicationKind.Unity,
        ["tuanjiehub"] = LocalApplicationKind.TuanjieHub,
        ["TuanjieHub"] = LocalApplicationKind.TuanjieHub,
        ["Tuanjie Hub"] = LocalApplicationKind.TuanjieHub,
        ["Tuanjie"] = LocalApplicationKind.Tuanjie,
        ["godot"] = LocalApplicationKind.Godot,
        ["Godot"] = LocalApplicationKind.Godot,
        ["godot4"] = LocalApplicationKind.Godot,
        ["godot-mono"] = LocalApplicationKind.Godot
    };

    private static void AddLinuxKnownPaths(ICollection<(LocalApplicationKind, string)> candidates)
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (Environment.GetEnvironmentVariable("RIDER_HOME") is { Length: > 0 } riderHome)
        {
            AddLinuxRiderHome(candidates, riderHome);
        }
        AddLinuxRiderInstallations(candidates, "/opt");
        AddLinuxRiderInstallations(candidates, Path.Combine(data, "JetBrains", "Toolbox", "apps"));

        AddFile(candidates, LocalApplicationKind.VisualStudioCode, "/usr/bin/code");
        AddFile(candidates, LocalApplicationKind.VisualStudioCode, "/snap/bin/code");
        AddFile(candidates, LocalApplicationKind.UnityHub, "/opt/unityhub/unityhub");
        AddFile(candidates, LocalApplicationKind.UnityHub, "/usr/bin/unityhub");
        AddFile(candidates, LocalApplicationKind.TuanjieHub, "/opt/tuanjiehub/tuanjiehub");
        AddFile(candidates, LocalApplicationKind.TuanjieHub, "/usr/bin/tuanjiehub");
        AddEngine(LocalApplicationKind.Unity, "Unity", "UNITY_HOME");
        AddEngine(LocalApplicationKind.Tuanjie, "Tuanjie", "TUANJIE_HOME");

        if (Environment.GetEnvironmentVariable("GODOT_HOME") is { Length: > 0 } godotHome)
        {
            foreach (var (name, kind) in LinuxLaunchers.Where(entry => entry.Value == LocalApplicationKind.Godot))
            {
                AddFile(candidates, kind, Path.Combine(godotHome, name));
            }
            AddFiles(candidates, LocalApplicationKind.Godot, godotHome, "Godot_v*", recursive: false);
        }

        void AddEngine(LocalApplicationKind kind, string name, string variable)
        {
            AddVersionedEditorFiles(candidates, kind, Path.Combine(user, name, "Hub", "Editor"), "Editor", name);
            AddEnvironmentHome(candidates, kind, variable, "Editor", name);
            AddEnvironmentHome(candidates, kind, variable, name);
        }
    }

    private static void AddLinuxRiderInstallations(ICollection<(LocalApplicationKind, string)> candidates, string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }
        try
        {
            foreach (var home in Directory.EnumerateDirectories(root))
            {
                AddLinuxRiderHome(candidates, home);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Toolbox and /opt are independent optional installation roots.
        }
    }

    private static void AddLinuxRiderHome(ICollection<(LocalApplicationKind, string)> candidates, string home)
    {
        var script = Path.Combine(home, "bin", "rider.sh");
        AddFile(candidates, LocalApplicationKind.Rider,
            ApplicationExecutable.IsRunnable(script) ? script : Path.Combine(home, "bin", "rider"));
    }

    private static void AddLinuxPathCandidates(ICollection<(LocalApplicationKind, string)> candidates, string directory)
    {
        foreach (var (name, kind) in LinuxLaunchers)
        {
            AddFile(candidates, kind, Path.Combine(directory, name));
        }
        AddFiles(candidates, LocalApplicationKind.Godot, directory, "Godot_v*", recursive: false);
    }

    internal static bool MatchesLinuxExecutable(LocalApplicationKind kind, string name)
    {
        if (LinuxLaunchers.TryGetValue(name, out var known))
        {
            return known == kind;
        }
        if (kind == LocalApplicationKind.Godot && name.StartsWith("Godot_v", StringComparison.Ordinal))
        {
            // Both standard and .NET archives end in _linux.<architecture>.
            return Path.GetFileNameWithoutExtension(name).EndsWith("_linux", StringComparison.Ordinal) &&
                   Path.GetExtension(name) is ".x86_64" or ".x86_32" or ".arm64" or ".arm32";
        }
        return kind == LocalApplicationKind.Other;
    }
}
