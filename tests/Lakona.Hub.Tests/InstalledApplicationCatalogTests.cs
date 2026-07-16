using Lakona.Hub.Applications;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class InstalledApplicationCatalogTests
{
    [Fact]
    public void Detect_PrefersRiderThenVisualStudioThenVsCodeAndDeduplicatesPaths()
    {
        using var executables = TestExecutables.Create("rider64.exe", "devenv.exe", "Code.exe", "CustomIde.exe");
        var rider = new LocalApplicationInstallation(LocalApplicationKind.Rider, "Rider", executables["rider64.exe"]);
        var visualStudio = new LocalApplicationInstallation(LocalApplicationKind.VisualStudio, "Visual Studio", executables["devenv.exe"]);
        var vsCode = new LocalApplicationInstallation(LocalApplicationKind.VisualStudioCode, "VS Code", executables["Code.exe"]);
        var customIde = new LocalApplicationInstallation(LocalApplicationKind.Other, "Custom IDE", executables["CustomIde.exe"]);
        var source = new FakeProbeSource([customIde, vsCode, rider, visualStudio, rider]);

        var detected = new InstalledApplicationCatalog(source).Detect();
        var editors = InstalledApplicationCatalog.ServerEditors(detected);

        Assert.Equal(["Rider", "Visual Studio", "VS Code", "Custom IDE"], editors.Select(editor => editor.DisplayName));
    }

    [Fact]
    public void Detect_KeepsUnityHubAndEveryUnityEditorInstallation()
    {
        using var executables = TestExecutables.Create("Unity Hub.exe", "Unity-2022.exe", "Unity-6.exe");
        var source = new FakeProbeSource(
        [
            new(LocalApplicationKind.Unity, "Unity", executables["Unity-2022.exe"], "2022.3"),
            new(LocalApplicationKind.UnityHub, "Unity Hub", executables["Unity Hub.exe"], "3.16"),
            new(LocalApplicationKind.Unity, "Unity", executables["Unity-6.exe"], "6000.3")
        ]);

        var detected = new InstalledApplicationCatalog(source).Detect();

        Assert.Equal(
            [LocalApplicationKind.UnityHub, LocalApplicationKind.Unity, LocalApplicationKind.Unity],
            detected.Select(application => application.Kind));
        Assert.Equal(["3.16", "6000.3", "2022.3"], detected.Select(application => application.Version));
    }

    [Fact]
    public void Detect_OrdersTuanjieProductVersionsNumerically()
    {
        using var executables = TestExecutables.Create("Tuanjie-167.exe", "Tuanjie-168.exe", "Tuanjie-1610.exe");
        var source = new FakeProbeSource(
        [
            new(LocalApplicationKind.Tuanjie, "Tuanjie", executables["Tuanjie-167.exe"], "1.6.7"),
            new(LocalApplicationKind.Tuanjie, "Tuanjie", executables["Tuanjie-168.exe"], "1.6.8"),
            new(LocalApplicationKind.Tuanjie, "Tuanjie", executables["Tuanjie-1610.exe"], "1.6.10")
        ]);

        var detected = new InstalledApplicationCatalog(source).Detect();

        Assert.Equal(["1.6.10", "1.6.8", "1.6.7"], detected.Select(application => application.Version));
    }

    [Fact]
    public void TryCreateInstallation_RecognizesTuanjieHubAndEditorVersionLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));
        var hub = Path.Combine(root, "Tuanjie Hub", "Tuanjie Hub.exe");
        var editor = Path.Combine(root, "Tuanjie", "Hub", "Editor", "2022.3.999t1", "Editor", "Tuanjie.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(hub)!);
            Directory.CreateDirectory(Path.GetDirectoryName(editor)!);
            File.WriteAllText(hub, string.Empty);
            File.WriteAllText(editor, string.Empty);

            Assert.True(SystemApplicationProbeSource.TryCreateInstallation(
                LocalApplicationKind.TuanjieHub,
                hub,
                out var hubInstallation));
            Assert.Equal("Tuanjie Hub", hubInstallation.DisplayName);

            Assert.True(SystemApplicationProbeSource.TryCreateInstallation(
                LocalApplicationKind.Tuanjie,
                editor,
                out var editorInstallation));
            Assert.Equal("2022.3.999t1", editorInstallation.Version);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveTuanjieVersion_UsesHubProductVersionMappingAndFallsBackSafely()
    {
        var root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));
        var mapping = Path.Combine(root, "versionMapping.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(mapping, "{\"2022.3.61t8\":\"1.6.7\"}");

            Assert.Equal("1.6.7", SystemApplicationProbeSource.ResolveTuanjieVersion("2022.3.61t8", mapping));
            Assert.Equal("2022.3.61t99", SystemApplicationProbeSource.ResolveTuanjieVersion("2022.3.61t99", mapping));
            Assert.Equal("2022.3.61t8", SystemApplicationProbeSource.ResolveTuanjieVersion(
                "2022.3.61t8",
                Path.Combine(root, "missing.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Detect_IgnoresMissingAndRelativeExecutables()
    {
        var source = new FakeProbeSource(
        [
            new(LocalApplicationKind.Rider, "Rider", "rider64.exe"),
            new(LocalApplicationKind.Godot, "Godot", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"))
        ]);

        Assert.Empty(new InstalledApplicationCatalog(source).Detect());
    }

    [Fact]
    public void MergePreferred_PutsManualPathFirstAndKeepsOtherDetectedVersions()
    {
        var automatic = new[]
        {
            new LocalApplicationInstallation(LocalApplicationKind.Unity, "Unity", @"C:\Unity\6000.3\Unity.exe", "6000.3"),
            new LocalApplicationInstallation(LocalApplicationKind.Unity, "Unity", @"C:\Unity\2022.3\Unity.exe", "2022.3")
        };
        var manual = new[]
        {
            new LocalApplicationInstallation(LocalApplicationKind.Unity, "Unity", @"D:\Unity\Unity.exe", "6000.2")
        };

        var merged = InstalledApplicationCatalog.MergePreferred(automatic, manual);

        Assert.Equal(@"D:\Unity\Unity.exe", merged[0].ExecutablePath);
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void TryCreateInstallation_ValidatesExecutableForSelectedKind()
    {
        using var executables = TestExecutables.Create("rider64.exe", "Godot_v4.6.exe");

        Assert.True(SystemApplicationProbeSource.TryCreateInstallation(
            LocalApplicationKind.Rider,
            executables["rider64.exe"],
            out var rider));
        Assert.Equal(LocalApplicationKind.Rider, rider.Kind);
        Assert.False(SystemApplicationProbeSource.TryCreateInstallation(
            LocalApplicationKind.Unity,
            executables["Godot_v4.6.exe"],
            out _));
    }

    [Fact]
    public void TryCreateManualInstallation_RecognizesEngineHubsAndAcceptsArbitraryIde()
    {
        using var executables = TestExecutables.Create("Unity Hub.exe", "Tuanjie Hub.exe", "CustomIde.exe");

        Assert.True(SystemApplicationProbeSource.TryCreateManualInstallation(
            executables["Unity Hub.exe"],
            out var unityHub));
        Assert.Equal(LocalApplicationKind.UnityHub, unityHub.Kind);

        Assert.True(SystemApplicationProbeSource.TryCreateManualInstallation(
            executables["Tuanjie Hub.exe"],
            out var tuanjieHub));
        Assert.Equal(LocalApplicationKind.TuanjieHub, tuanjieHub.Kind);

        Assert.True(SystemApplicationProbeSource.TryCreateManualInstallation(
            executables["CustomIde.exe"],
            out var customIde));
        Assert.Equal(LocalApplicationKind.Other, customIde.Kind);
        Assert.Equal("CustomIde", customIde.DisplayName);
    }

    [Fact]
    public void TryCreateInstallation_ResolvesMacApplicationBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));
        var bundle = Path.Combine(root, "Rider.app");
        var executable = Path.Combine(bundle, "Contents", "MacOS", "rider");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, string.Empty);

            Assert.True(SystemApplicationProbeSource.TryCreateInstallation(
                LocalApplicationKind.Rider,
                bundle,
                out var installation));
            Assert.Equal(executable, installation.ExecutablePath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeProbeSource(IReadOnlyList<LocalApplicationInstallation> applications) : IApplicationProbeSource
    {
        public IEnumerable<LocalApplicationInstallation> FindApplications() => applications;
    }

    private sealed class TestExecutables : IDisposable
    {
        private readonly string root;

        private TestExecutables(string root)
        {
            this.root = root;
        }

        public string this[string name] => Path.Combine(root, name);

        public static TestExecutables Create(params string[] names)
        {
            var executables = new TestExecutables(Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(executables.root);
            foreach (var name in names)
            {
                File.WriteAllText(executables[name], string.Empty);
            }

            return executables;
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
