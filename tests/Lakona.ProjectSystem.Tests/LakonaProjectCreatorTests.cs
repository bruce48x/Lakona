using Lakona.ProjectSystem;
using Lakona.Tool.Execution;
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

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Lakona.ProjectSystem.Creator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
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
}
