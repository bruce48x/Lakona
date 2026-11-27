using Lakona.Hub.Applications;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

// These tests exercise discovery through the same process environment as the desktop application.
[CollectionDefinition("Linux application discovery", DisableParallelization = true)]
public sealed class LinuxApplicationDiscoveryCollection;

[Collection("Linux application discovery")]
public sealed class LinuxApplicationDiscoveryTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("Lakona.Hub.Apps.").FullName;
    private readonly Dictionary<string, string?> previousEnvironment = [];
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "Requires Linux filesystem and process semantics.", SkipUnless = nameof(IsLinux))]
    public void DetectFindsNativeLaunchersAndIgnoresWindowsNamesAndNonExecutableFiles()
    {
        Set("PATH", $"{root}/bin:{root}/Bin:{root}/aliases:{root}/noexec");
        Set("RIDER_HOME", root + "/rider-home");
        Set("UNITY_HOME", root + "/unity-home");
        Set("TUANJIE_HOME", root + "/tuanjie-home");
        Set("GODOT_HOME", root + "/godot-home");
        Set("XDG_DATA_HOME", root + "/data");
        Set("XDG_CONFIG_HOME", root + "/config");
        Set("HOME", root + "/home");
        Directory.CreateDirectory(root + "/config/TuanjieHub");
        File.WriteAllText(root + "/config/TuanjieHub/versionMapping.json", "[]");
        var expected = new (string Path, LocalApplicationKind Kind)[]
        {
            ("bin/rider", LocalApplicationKind.Rider), ("Bin/rider", LocalApplicationKind.Rider),
            ("rider-home/bin/rider", LocalApplicationKind.Rider),
            ("data/JetBrains/Toolbox/apps/Rider/bin/rider", LocalApplicationKind.Rider),
            ("bin/code", LocalApplicationKind.VisualStudioCode), ("bin/unityhub", LocalApplicationKind.UnityHub),
            ("bin/tuanjiehub", LocalApplicationKind.TuanjieHub), ("bin/Unity", LocalApplicationKind.Unity),
            ("bin/Tuanjie", LocalApplicationKind.Tuanjie), ("bin/godot", LocalApplicationKind.Godot),
            ("unity-home/Editor/Unity", LocalApplicationKind.Unity), ("tuanjie-home/Editor/Tuanjie", LocalApplicationKind.Tuanjie),
            ("home/Unity/Hub/Editor/6000.3.13f1/Editor/Unity", LocalApplicationKind.Unity),
            ("home/Tuanjie/Hub/Editor/2022.3.61t8/Editor/Tuanjie", LocalApplicationKind.Tuanjie),
            ("godot-home/Godot_v4.6-stable_mono_linux.x86_64", LocalApplicationKind.Godot),
            ("bin/Godot_v4.6-stable_linux.arm64", LocalApplicationKind.Godot)
        };
        foreach (var (path, _) in expected)
        {
            Create(path);
        }
        Create("bin/devenv.exe");
        Create("bin/Code.exe");
        Create("bin/Godot_v4.6-stable_mono_linux.x86_64.zip");
        Create("noexec/rider");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(root + "/noexec/rider", UnixFileMode.UserRead);
        }
        Directory.CreateDirectory(root + "/aliases");
        File.CreateSymbolicLink(root + "/aliases/unityhub", root + "/bin/unityhub");

        var detected = new InstalledApplicationCatalog().Detect()
            .Where(app => app.ExecutablePath.StartsWith(root + "/", StringComparison.Ordinal)).ToArray();

        Assert.Equal(expected.Length, detected.Length);
        foreach (var (path, kind) in expected)
        {
            Assert.Contains(detected, app => app.Kind == kind && app.ExecutablePath == Path.Combine(root, path));
        }
        Assert.Contains(detected, app => app.Version == "2022.3.61t8");
        Assert.False(SystemApplicationProbeSource.TryCreateManualInstallation(root + "/noexec/rider", out _));
        Assert.True(SystemApplicationProbeSource.TryCreateManualInstallation(root + "/godot-home/Godot_v4.6-stable_mono_linux.x86_64", out var godot));
        Assert.Equal(["--editor", "--path", root], ApplicationLaunchPlanner.OpenClientDirectory(root, LakonaProjectClient.Godot, godot).Arguments);
    }

    [Theory(Skip = "Requires Linux symbolic links.", SkipUnless = nameof(IsLinux))]
    [InlineData("user", "--user")]
    [InlineData("system", "--system")]
    public void ManualFlatpakRegistrationUsesStableDeploymentAfterRestart(string scope, string scopeArgument)
    {
        Set("FLATPAK_USER_DIR", root + "/user");
        Set("FLATPAK_SYSTEM_DIR", root + "/system");
        Set("PATH", root + "/bin");
        Create("bin/flatpak");
        var app = $"{scope}/app/com.jetbrains.Rider";
        var bin = $"{app}/x86_64/stable/revision/files/bin";
        var picked = Create(bin + "/rider");
        Directory.CreateSymbolicLink($"{root}/{app}/current", "x86_64/stable");
        Directory.CreateSymbolicLink($"{root}/{app}/x86_64/stable/active", "revision");

        Assert.True(SystemApplicationProbeSource.TryCreateManualInstallation(picked, out var rider));
        var stable = $"{root}/{app}/current/active/files/bin/rider";
        Assert.Equal(stable, rider.ExecutablePath);
        var store = new ManualApplicationStore(root + "/settings/apps.json");
        store.Save([new(rider.Kind, rider.DisplayName, stable)]);
        var branch = $"{root}/{app}/x86_64/stable";
        Create($"{app}/x86_64/stable/updated/files/bin/rider");
        Directory.Delete(branch + "/active");
        Directory.CreateSymbolicLink(branch + "/active", "updated");
        File.Delete(picked);
        using var restarted = new HubApplicationRegistry(new InstalledApplicationCatalog(), store,
            new HubLocalization(HubLanguage.English), []);
        var restored = Assert.Single(restarted.InstalledApplications);
        var plan = ApplicationLaunchPlanner.OpenServerDirectory(root, restored);
        Assert.Equal("flatpak", Path.GetFileName(plan.ExecutablePath));
        Assert.Equal(["run", scopeArgument, "com.jetbrains.Rider", root], plan.Arguments);
    }

    [Fact(Skip = "Requires Linux case-sensitive paths.", SkipUnless = nameof(IsLinux))]
    public void OldGenericRegistrationsBecomeClientToolsAndCaseDistinctPreferencesSurvive()
    {
        var godot = Create("Godot_v4.6-stable_mono_linux.x86_64");
        var hub = Create("tuanjiehub");
        var lower = Create("bin/rider");
        var upper = Create("Bin/rider");
        var store = new ManualApplicationStore(root + "/apps.json");
        store.Save([
            new(LocalApplicationKind.Other, "Godot", godot), new(LocalApplicationKind.Other, "tuanjiehub", hub),
            new(LocalApplicationKind.Rider, "Rider A", lower), new(LocalApplicationKind.Rider, "Rider B", upper)]);
        using var registry = new HubApplicationRegistry(new InstalledApplicationCatalog(), store,
            new HubLocalization(HubLanguage.English), []);
        Assert.Contains(registry.Tools, tool => tool.Kind == LocalApplicationKind.Godot && tool.ManualPath == godot);
        Assert.Contains(registry.Tools, tool => tool.Kind == LocalApplicationKind.TuanjieHub && tool.ManualPath == hub);
        var selection = new ServerEditorSelection(upper);
        selection.Refresh(registry.InstalledApplications);
        Assert.Equal(upper, selection.SelectedEditor?.ExecutablePath);
        Assert.True(registry.RemoveManual(Assert.Single(registry.Tools, tool => tool.ManualPath == lower)));
        Assert.Contains(store.Load(), item => item.ExecutablePath == upper);
        Assert.DoesNotContain(store.Load(), item => item.ExecutablePath == lower);
    }

    private string Create(string path) => TestApplicationExecutable.Create(Path.Combine(root, path));

    private void Set(string name, string value)
    {
        previousEnvironment.Add(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        foreach (var (name, value) in previousEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
        Directory.Delete(root, recursive: true);
    }
}
