using Lakona.Hub.Applications;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class InstalledApplicationCatalogTests
{
    [Fact]
    public void Detect_PrefersRiderThenVisualStudioThenVsCodeAndDeduplicatesPaths()
    {
        using var executables = TestExecutables.Create("rider64.exe", "devenv.exe", "Code.exe");
        var rider = new LocalApplicationInstallation(LocalApplicationKind.Rider, "Rider", executables["rider64.exe"]);
        var visualStudio = new LocalApplicationInstallation(LocalApplicationKind.VisualStudio, "Visual Studio", executables["devenv.exe"]);
        var vsCode = new LocalApplicationInstallation(LocalApplicationKind.VisualStudioCode, "VS Code", executables["Code.exe"]);
        var source = new FakeProbeSource([vsCode, rider, visualStudio, rider]);

        var detected = new InstalledApplicationCatalog(source).Detect();
        var editors = InstalledApplicationCatalog.ServerEditors(detected);

        Assert.Equal(["Rider", "Visual Studio", "VS Code"], editors.Select(editor => editor.DisplayName));
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
