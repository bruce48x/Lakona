using Lakona.ProjectSystem;
using Lakona.ProjectSystem.Generation.Execution;
using Lakona.ProjectSystem.Generation.Domain;
using Lakona.ProjectSystem.Generation.Planning;
using Xunit;

namespace Lakona.ProjectSystem.Tests;

public sealed class LakonaProjectCreatorTests
{
    [Fact]
    public async Task CreateAsync_GeneratesCompleteConsoleProjectThroughSharedFacade()
    {
        var outputRoot = CreateTempRoot();
        try
        {
            var request = new LakonaProjectCreationRequest(
                "SharedFacadeGame",
                outputRoot,
                LakonaClientEngine.Console,
                Transport: LakonaTransport.WebSocket,
                Serializer: LakonaSerializer.Json,
                DeploymentProfile: LakonaDeploymentProfile.Compose);

            var result = await new LakonaProjectCreator(new GitUnavailableRunner()).CreateAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(Path.Combine(outputRoot, "SharedFacadeGame"), result.RootPath);
            Assert.True(File.Exists(Path.Combine(result.RootPath, "Shared", "Shared.csproj")));
            Assert.True(File.Exists(Path.Combine(result.RootPath, "Server", "App", "Server.App.csproj")));
            Assert.True(File.Exists(Path.Combine(result.RootPath, "Server", "Hotfix", "Server.Hotfix.csproj")));
            Assert.True(File.Exists(Path.Combine(result.RootPath, "Client", "Client.csproj")));
            Assert.True(File.Exists(Path.Combine(result.RootPath, "docker-compose.cluster.yml")));
            AssertSkillPackMatchesRepository(result.RootPath);

            var clientProject = await File.ReadAllTextAsync(
                Path.Combine(result.RootPath, "Client", "Client.csproj"),
                TestContext.Current.CancellationToken);
            Assert.Contains("Lakona.Rpc.Transport.WebSocket", clientProject, StringComparison.Ordinal);
            Assert.Contains("Lakona.Rpc.Serializer.Json", clientProject, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_RejectsNonEmptyTargetBeforeReplacingUserFiles()
    {
        var outputRoot = CreateTempRoot();
        var targetRoot = Path.Combine(outputRoot, "ExistingGame");
        Directory.CreateDirectory(targetRoot);
        var existingFile = Path.Combine(targetRoot, "keep.txt");
        await File.WriteAllTextAsync(existingFile, "user data", TestContext.Current.CancellationToken);

        try
        {
            await Assert.ThrowsAsync<LakonaProjectCreationException>(() =>
                new LakonaProjectCreator(new GitUnavailableRunner()).CreateAsync(
                    new LakonaProjectCreationRequest("ExistingGame", outputRoot, LakonaClientEngine.Console),
                    TestContext.Current.CancellationToken));

            Assert.Equal("user data", await File.ReadAllTextAsync(existingFile, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_DoesNotCreateUnityTargetWhenDependencyRestoreFails()
    {
        var outputRoot = CreateTempRoot();
        try
        {
            var creator = new LakonaProjectCreator(new GitUnavailableRunner(), new FailingUnityDependencyRestorer());

            await Assert.ThrowsAsync<LakonaProjectCreationException>(() => creator.CreateAsync(
                new LakonaProjectCreationRequest("RestoreFailure", outputRoot, LakonaClientEngine.Unity),
                TestContext.Current.CancellationToken));

            Assert.False(Directory.Exists(Path.Combine(outputRoot, "RestoreFailure")));
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_PublishesRestoredUnityPackagesInsideGeneratedProject()
    {
        var outputRoot = CreateTempRoot();
        try
        {
            var creator = new LakonaProjectCreator(new GitUnavailableRunner(), new SuccessfulUnityDependencyRestorer());
            var result = await creator.CreateAsync(
                new LakonaProjectCreationRequest("RestoredGame", outputRoot, LakonaClientEngine.Unity),
                TestContext.Current.CancellationToken);

            Assert.Equal("restored", await File.ReadAllTextAsync(
                Path.Combine(result.RootPath, "Client", "Assets", "Packages", "Example.1.0.0", "Example.dll"),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_ReportsEachProjectCreationStageInOrder()
    {
        var outputRoot = CreateTempRoot();
        try
        {
            var stages = new List<LakonaProjectCreationStage>();
            var progress = new RecordingProgress<LakonaProjectCreationProgress>(value => stages.Add(value.Stage));

            await new LakonaProjectCreator(new GitUnavailableRunner(), new SuccessfulUnityDependencyRestorer()).CreateAsync(
                new LakonaProjectCreationRequest("ProgressGame", outputRoot, LakonaClientEngine.Unity),
                progress,
                TestContext.Current.CancellationToken);

            Assert.Equal(
                [
                    LakonaProjectCreationStage.Preparing,
                    LakonaProjectCreationStage.RestoringClientDependencies,
                    LakonaProjectCreationStage.WritingProject,
                    LakonaProjectCreationStage.InitializingGit,
                    LakonaProjectCreationStage.Completed
                ],
                stages);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Lakona.ProjectSystem.Creator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AssertSkillPackMatchesRepository(string projectRoot)
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "skills");
        var generatedRoot = Path.Combine(projectRoot, ".agents", "skills");
        var expected = ReadRelativeFiles(sourceRoot);
        var actual = ReadRelativeFiles(generatedRoot);

        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var path in expected.Keys)
        {
            Assert.Equal(NormalizeText(expected[path]), actual[path]);
        }
    }

    private static string NormalizeText(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }

    private static SortedDictionary<string, string> ReadRelativeFiles(string root)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            files.Add(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllText(path));
        }

        return files;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CONTRIBUTING.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the Lakona repository root.");
    }

    private sealed class GitUnavailableRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            string[] arguments,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Git is intentionally unavailable in this test.");
        }
    }

    private sealed class FailingUnityDependencyRestorer : IUnityDependencyRestorer
    {
        public Task<RestoredUnityDependencies?> RestoreAsync(
            LakonaProjectSpec spec,
            GenerationPlan plan,
            CancellationToken cancellationToken) =>
            throw new LakonaProjectCreationException("Editor restore failed.");
    }

    private sealed class SuccessfulUnityDependencyRestorer : IUnityDependencyRestorer
    {
        public async Task<RestoredUnityDependencies?> RestoreAsync(
            LakonaProjectSpec spec,
            GenerationPlan plan,
            CancellationToken cancellationToken)
        {
            var root = Path.Combine(Path.GetTempPath(), "Lakona.ProjectSystem.Restore.Tests", Guid.NewGuid().ToString("N"));
            var package = Path.Combine(root, "Example.1.0.0");
            Directory.CreateDirectory(package);
            await File.WriteAllTextAsync(Path.Combine(package, "Example.dll"), "restored", cancellationToken);
            return new RestoredUnityDependencies(root);
        }
    }

    private sealed class RecordingProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

}
