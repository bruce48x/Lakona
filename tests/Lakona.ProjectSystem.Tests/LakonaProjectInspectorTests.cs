using Lakona.ProjectSystem;
using Xunit;

namespace Lakona.ProjectSystem.Tests;

public sealed class LakonaProjectInspectorTests
{
    [Fact]
    public void Inspect_RecognizesUnityProjectWithoutWritingProjectData()
    {
        using var project = TestProject.Create();
        project.Write("Client/ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.3.3f1\n");
        var before = project.Snapshot();

        var result = new LakonaProjectInspector().Inspect(project.RootPath);

        Assert.Equal(LakonaProjectStatus.Ready, result.Status);
        Assert.Equal(LakonaProjectClient.Unity, result.Client);
        Assert.Equal("6000.3.3f1", result.ClientVersion);
        Assert.Equal("0.25.19", result.LakonaVersion);
        Assert.Equal(before, project.Snapshot());
        Assert.False(Directory.Exists(Path.Combine(project.RootPath, ".lakona")));
    }

    [Fact]
    public void Inspect_PrefersCanonicalClientDirectoryOverOtherClientMarkers()
    {
        using var project = TestProject.Create();
        project.Write(
            "Client/ProjectSettings/ProjectVersion.txt",
            "m_EditorVersion: 2022.3.61t8\n" +
            "m_TuanjieEditorVersion: 1.6.7\n");
        project.Write("OtherClient/ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.3.3f1\n");

        var result = new LakonaProjectInspector().Inspect(project.RootPath);

        Assert.Equal(LakonaProjectStatus.Ready, result.Status);
        Assert.Equal(LakonaProjectClient.Tuanjie, result.Client);
        Assert.Equal("1.6.7", result.ClientVersion);
        Assert.Equal(Path.Combine(project.RootPath, "Client"), result.ClientPath);
        Assert.Equal(Path.Combine(project.RootPath, "Server"), result.ServerPath);
    }

    [Fact]
    public void Inspect_FallsBackToTopLevelDirectoriesForEveryProjectPart()
    {
        using var project = TestProject.Create();
        Directory.Move(
            Path.Combine(project.RootPath, "Shared"),
            Path.Combine(project.RootPath, "Contracts"));
        Directory.Move(
            Path.Combine(project.RootPath, "Server"),
            Path.Combine(project.RootPath, "Backend"));
        project.Write(
            "TuanjieClient/ProjectSettings/ProjectVersion.txt",
            "m_EditorVersion: 2022.3.61t8\n" +
            "m_TuanjieEditorVersion: 1.6.7\n");

        var result = new LakonaProjectInspector().Inspect(project.RootPath);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(LakonaProjectStatus.Ready, result.Status);
        Assert.Equal(LakonaProjectClient.Tuanjie, result.Client);
        Assert.Equal("1.6.7", result.ClientVersion);
        Assert.Equal(Path.Combine(project.RootPath, "Backend"), result.ServerPath);
        Assert.Equal(Path.Combine(project.RootPath, "TuanjieClient"), result.ClientPath);
    }

    [Fact]
    public void Inspect_RecognizesGodotProject()
    {
        using var project = TestProject.Create();
        project.Write("Client/Client.csproj", "<Project />\n");
        project.Write(
            "Client/project.godot",
            "config_version=5\nconfig/features=PackedStringArray(\"4.6\", \"C#\")\n");

        var result = new LakonaProjectInspector().Inspect(project.RootPath);

        Assert.Equal(LakonaProjectStatus.Ready, result.Status);
        Assert.Equal(LakonaProjectClient.Godot, result.Client);
        Assert.Equal("4.6", result.ClientVersion);
    }

    [Fact]
    public void Inspect_RecognizesConsoleProject()
    {
        using var project = TestProject.Create();
        project.Write("Client/Client.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");

        var result = new LakonaProjectInspector().Inspect(project.RootPath);

        Assert.Equal(LakonaProjectStatus.Ready, result.Status);
        Assert.Equal(LakonaProjectClient.Console, result.Client);
        Assert.Null(result.ClientVersion);
    }

    [Fact]
    public void Inspect_ReturnsIncompleteWhenARequiredProjectFileIsMissing()
    {
        using var project = TestProject.Create();
        File.Delete(Path.Combine(project.RootPath, "Server", "Hotfix", "Server.Hotfix.csproj"));
        project.Write("Client/Client.csproj", "<Project />\n");

        var result = new LakonaProjectInspector().Inspect(project.RootPath);

        Assert.Equal(LakonaProjectStatus.Incomplete, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "missing-project-file" &&
            diagnostic.Message.Contains("Server/Hotfix/Server.Hotfix.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_DoesNotResolveDocumentTypeDeclarations()
    {
        using var project = TestProject.Create();
        project.Write("Client/Client.csproj", "<Project />\n");
        project.Write(
            "Server/App/Server.App.csproj",
            "<!DOCTYPE Project [<!ENTITY probe SYSTEM \"file:///does-not-exist\">]><Project>&probe;</Project>");

        var result = new LakonaProjectInspector().Inspect(project.RootPath);

        Assert.Null(result.LakonaVersion);
        Assert.Equal(LakonaProjectStatus.Incomplete, result.Status);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "server-project-unreadable");
    }

    [Fact]
    public void Inspect_RejectsOversizedClientMetadata()
    {
        using var project = TestProject.Create();
        project.Write(
            "Client/ProjectSettings/ProjectVersion.txt",
            "m_EditorVersion: 6000.3.3f1\n" + new string('x', 64 * 1024));

        var result = new LakonaProjectInspector().Inspect(project.RootPath);

        Assert.Equal(LakonaProjectStatus.Incomplete, result.Status);
        Assert.Equal(LakonaProjectClient.Unknown, result.Client);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "client-version-unreadable");
    }

    [Fact]
    public void Inspect_ReturnsNotFoundForMissingDirectory()
    {
        var result = new LakonaProjectInspector().Inspect(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Equal(LakonaProjectStatus.NotFound, result.Status);
        Assert.False(result.IsRecognized);
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
            var project = new TestProject(Path.Combine(Path.GetTempPath(), "Lakona.ProjectSystem.Tests", Guid.NewGuid().ToString("N")));
            project.Write("Shared/Shared.csproj", "<Project />\n");
            project.Write("Server/Server.slnx", "<Solution />\n");
            project.Write(
                "Server/App/Server.App.csproj",
                "<Project><ItemGroup><PackageReference Include=\"Lakona.Game.Server\" Version=\"0.25.19\" /></ItemGroup></Project>\n");
            project.Write("Server/Hotfix/Server.Hotfix.csproj", "<Project />\n");
            return project;
        }

        public void Write(string relativePath, string content)
        {
            var path = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public string[] Snapshot()
        {
            return Directory.GetFiles(RootPath, "*", SearchOption.AllDirectories)
                .Select(path => $"{Path.GetRelativePath(RootPath, path)}:{File.ReadAllText(path)}")
                .Order(StringComparer.Ordinal)
                .ToArray();
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
