using System.ComponentModel;
using Lakona.Tool.Execution;
using Xunit;

namespace Lakona.Tool.Tests.Execution;

public sealed class GitInitializerTests
{
    [Fact]
    public async Task InitializeAsync_GitUnavailable_ReturnsSkippedGitUnavailable()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(1, "", "git: command not found"), "--version");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.SkippedGitUnavailable, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_ParentWorktree_ReturnsSkippedParentWorktree()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(0, "/home/user/repos", ""), "rev-parse", "--show-toplevel");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.SkippedParentWorktree, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_AlreadyCommitted_ReturnsSkippedAlreadyCommitted()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(0, "/tmp/project", ""), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(0, "abc123", ""), "rev-parse", "--verify", "HEAD");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.SkippedAlreadyCommitted, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_EmptyRepo_InitializesAndCommits()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(0, "/tmp/project", ""), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(128, "", "fatal: Needed a single revision"), "rev-parse", "--verify", "HEAD");
        fake.Setup(new GitCommandResult(0, "", ""), "branch", "-M", "main");
        fake.Setup(new GitCommandResult(0, "Test User", ""), "config", "user.name");
        fake.Setup(new GitCommandResult(0, "test@example.com", ""), "config", "user.email");
        fake.Setup(new GitCommandResult(0, "", ""), "add", "-A");
        fake.Setup(new GitCommandResult(0, "?? README.md", ""), "status", "--porcelain");
        fake.Setup(new GitCommandResult(0, "[main abc123] Initial Lakona project", ""), "commit", "-m", "Initial Lakona project");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.InitializedAndCommitted, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_NewRepo_InitializesAndCommits()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(128, "", "fatal: not a git repository"), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(0, "Initialized empty Git repository", ""), "init", "-b", "main");
        fake.Setup(new GitCommandResult(0, "Test User", ""), "config", "user.name");
        fake.Setup(new GitCommandResult(0, "test@example.com", ""), "config", "user.email");
        fake.Setup(new GitCommandResult(0, "", ""), "add", "-A");
        fake.Setup(new GitCommandResult(0, "?? README.md", ""), "status", "--porcelain");
        fake.Setup(new GitCommandResult(0, "[main abc123] Initial Lakona project", ""), "commit", "-m", "Initial Lakona project");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.InitializedAndCommitted, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_InitNoBranchFlag_FallsBackToInitThenBranch()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.30.0", ""), "--version");
        fake.Setup(new GitCommandResult(128, "", "fatal: not a git repository"), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(129, "", "error: unknown switch `b'"), "init", "-b", "main");
        fake.Setup(new GitCommandResult(0, "Initialized empty Git repository", ""), "init");
        fake.Setup(new GitCommandResult(0, "", ""), "branch", "-M", "main");
        fake.Setup(new GitCommandResult(0, "Test User", ""), "config", "user.name");
        fake.Setup(new GitCommandResult(0, "test@example.com", ""), "config", "user.email");
        fake.Setup(new GitCommandResult(0, "", ""), "add", "-A");
        fake.Setup(new GitCommandResult(0, "?? README.md", ""), "status", "--porcelain");
        fake.Setup(new GitCommandResult(0, "[main abc123] Initial Lakona project", ""), "commit", "-m", "Initial Lakona project");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.InitializedAndCommitted, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_MissingIdentity_ReturnsInitializedNoCommitMissingIdentity()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(128, "", "fatal: not a git repository"), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(0, "Initialized empty Git repository", ""), "init", "-b", "main");
        fake.Setup(new GitCommandResult(0, "", ""), "config", "user.name");
        fake.Setup(new GitCommandResult(0, "", ""), "config", "user.email");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.InitializedNoCommitMissingIdentity, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_EmptyStatus_ReturnsInitializedNoCommitNoFiles()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(128, "", "fatal: not a git repository"), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(0, "Initialized empty Git repository", ""), "init", "-b", "main");
        fake.Setup(new GitCommandResult(0, "Test User", ""), "config", "user.name");
        fake.Setup(new GitCommandResult(0, "test@example.com", ""), "config", "user.email");
        fake.Setup(new GitCommandResult(0, "", ""), "add", "-A");
        fake.Setup(new GitCommandResult(0, "", ""), "status", "--porcelain");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.InitializedNoCommitNoFiles, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_CommitFails_ReturnsCommitFailed()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(128, "", "fatal: not a git repository"), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(0, "Initialized empty Git repository", ""), "init", "-b", "main");
        fake.Setup(new GitCommandResult(0, "Test User", ""), "config", "user.name");
        fake.Setup(new GitCommandResult(0, "test@example.com", ""), "config", "user.email");
        fake.Setup(new GitCommandResult(0, "", ""), "add", "-A");
        fake.Setup(new GitCommandResult(0, "?? README.md", ""), "status", "--porcelain");
        fake.Setup(new GitCommandResult(1, "", "error: gpg failed"), "commit", "-m", "Initial Lakona project");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.CommitFailed, result.Status);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task InitializeAsync_ProcessStartException_ReturnsSkippedGitUnavailable()
    {
        var fake = new ThrowingGitCommandRunner();

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.SkippedGitUnavailable, result.Status);
    }

    [Fact]
    public async Task InitializeAsync_EmptyRepoBranchRenameFails_ReturnsInitFailed()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(0, "/tmp/project", ""), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(128, "", "fatal: Needed a single revision"), "rev-parse", "--verify", "HEAD");
        fake.Setup(new GitCommandResult(1, "", "error: cannot rename branch"), "branch", "-M", "main");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.InitializationFailed, result.Status);
        Assert.Equal("unable to set main branch", result.Reason);
    }

    [Fact]
    public async Task InitializeAsync_CancellationDuringVersionCheck_PropagatesException()
    {
        var fake = new CancellationGitCommandRunner();

        var initializer = new GitInitializer(fake);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => initializer.InitializeAsync("/tmp/project", cts.Token));
    }

    [Fact]
    public async Task InitializeAsync_StatusFails_ReturnsCommitFailed()
    {
        var fake = new FakeGitCommandRunner();
        fake.Setup(new GitCommandResult(0, "git version 2.45.0", ""), "--version");
        fake.Setup(new GitCommandResult(128, "", "fatal: not a git repository"), "rev-parse", "--show-toplevel");
        fake.Setup(new GitCommandResult(0, "Initialized empty Git repository", ""), "init", "-b", "main");
        fake.Setup(new GitCommandResult(0, "Test User", ""), "config", "user.name");
        fake.Setup(new GitCommandResult(0, "test@example.com", ""), "config", "user.email");
        fake.Setup(new GitCommandResult(0, "", ""), "add", "-A");
        fake.Setup(new GitCommandResult(128, "", "fatal: index file corrupt"), "status", "--porcelain");

        var initializer = new GitInitializer(fake);
        var result = await initializer.InitializeAsync("/tmp/project", CancellationToken.None);

        Assert.Equal(GitInitializationStatus.CommitFailed, result.Status);
        Assert.Contains("index file corrupt", result.Reason, StringComparison.Ordinal);
    }

    private sealed class FakeGitCommandRunner : IGitCommandRunner
    {
        private readonly Dictionary<string, GitCommandResult> _setups = new(StringComparer.Ordinal);

        public void Setup(GitCommandResult result, params string[] args)
        {
            _setups[string.Join(" ", args)] = result;
        }

        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            string[] arguments,
            CancellationToken cancellationToken)
        {
            var key = string.Join(" ", arguments);
            if (_setups.TryGetValue(key, out var result))
            {
                return Task.FromResult(result);
            }

            throw new InvalidOperationException(
                $"No setup for: git {key}. Registered: {string.Join(", ", _setups.Keys)}");
        }
    }

    private sealed class ThrowingGitCommandRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            string[] arguments,
            CancellationToken cancellationToken)
        {
            throw new Win32Exception(2, "The system cannot find the file specified");
        }
    }

    private sealed class CancellationGitCommandRunner : IGitCommandRunner
    {
        public Task<GitCommandResult> RunAsync(
            string workingDirectory,
            string[] arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GitCommandResult(0, "", ""));
        }
    }
}
