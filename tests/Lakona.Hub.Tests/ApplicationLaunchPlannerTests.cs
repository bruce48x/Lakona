using Lakona.Hub.Applications;
using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class ApplicationLaunchPlannerTests
{
    [Fact]
    public void OpenServer_CreatesArgumentListWithoutShellComposition()
    {
        using var project = TestProject.Create();
        var cases = new[]
        {
            (LocalApplicationKind.Rider, "Server.slnx"),
            (LocalApplicationKind.VisualStudio, "Server.slnx"),
            (LocalApplicationKind.VisualStudioCode, "--reuse-window"),
            (LocalApplicationKind.Other, "Server.slnx")
        };

        foreach (var (kind, expectedArgument) in cases)
        {
            var editor = project.Application(kind);
            var plan = ApplicationLaunchPlanner.OpenServer(project.RootPath, editor);

            Assert.Equal(editor.ExecutablePath, plan.ExecutablePath);
            Assert.Contains(plan.Arguments, argument => argument.Contains(expectedArgument, StringComparison.Ordinal));
            Assert.DoesNotContain(plan.Arguments, argument => argument.Contains('"'));
        }
    }

    [Fact]
    public void OpenClient_CreatesEngineSpecificPlan()
    {
        using var project = TestProject.Create();
        var cases = new[]
        {
            (LakonaProjectClient.Unity, LocalApplicationKind.Unity, "-projectPath"),
            (LakonaProjectClient.Godot, LocalApplicationKind.Godot, "--editor")
        };

        foreach (var (client, applicationKind, expectedArgument) in cases)
        {
            var plan = ApplicationLaunchPlanner.OpenClient(project.RootPath, client, project.Application(applicationKind));

            Assert.Contains(expectedArgument, plan.Arguments);
            Assert.Equal(Path.Combine(project.RootPath, "Client"), plan.WorkingDirectory);
        }
    }

    [Fact]
    public void OpenClient_UsesSupportedCodeEditorForConsoleClient()
    {
        using var project = TestProject.Create();
        var cases = new[]
        {
            (LocalApplicationKind.Rider, "Client.csproj"),
            (LocalApplicationKind.VisualStudio, "Client.csproj"),
            (LocalApplicationKind.VisualStudioCode, "--reuse-window"),
            (LocalApplicationKind.Other, "Client.csproj")
        };

        foreach (var (kind, expectedArgument) in cases)
        {
            var plan = ApplicationLaunchPlanner.OpenClient(
                project.RootPath,
                LakonaProjectClient.Console,
                project.Application(kind));

            Assert.Contains(plan.Arguments, argument =>
                argument.Contains(expectedArgument, StringComparison.Ordinal));
            Assert.Equal(Path.Combine(project.RootPath, "Client"), plan.WorkingDirectory);
        }
    }

    [Fact]
    public void OpenClient_RejectsMismatchedApplication()
    {
        using var project = TestProject.Create();

        Assert.Throws<ArgumentException>(() => ApplicationLaunchPlanner.OpenClient(
            project.RootPath,
            LakonaProjectClient.Unity,
            project.Application(LocalApplicationKind.Godot)));
    }

    private sealed class TestProject : IDisposable
    {
        private TestProject(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TestProject Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "Lakona.Hub.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Server"));
            Directory.CreateDirectory(Path.Combine(root, "Client"));
            File.WriteAllText(Path.Combine(root, "Server", "Server.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(root, "Client", "Client.csproj"), "<Project />");
            return new TestProject(root);
        }

        public LocalApplicationInstallation Application(LocalApplicationKind kind)
        {
            var executable = Path.Combine(RootPath, kind + ".exe");
            File.WriteAllText(executable, string.Empty);
            return new LocalApplicationInstallation(kind, kind.ToString(), executable);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
