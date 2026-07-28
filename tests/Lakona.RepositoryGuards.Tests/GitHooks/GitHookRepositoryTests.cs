using System.Diagnostics;
using Lakona.RepositoryGuards.Tests.PackageVersions;
using Xunit;

namespace Lakona.RepositoryGuards.Tests.GitHooks;

public sealed class GitHookRepositoryTests
{
    [Fact]
    public void Install_script_configures_tracked_hooks_path()
    {
        using var fixture = GitHookFixture.Create();

        var result = RunPowerShell(
            fixture.RepositoryInstallScript,
            "-RepositoryRoot",
            fixture.Root);

        AssertPowerShellSucceeded(result);
        Assert.Equal(".githooks", GitRunner.Run(fixture.Root, "config", "--get", "core.hooksPath").Trim());
        var prePushHook = File.ReadAllText(Path.Combine(fixture.Root, ".githooks", "pre-push"));
        Assert.Contains("scripts/git/pre-push.ps1", prePushHook, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_script_retries_a_transient_git_config_lock()
    {
        using var fixture = GitHookFixture.Create();
        var configLockPath = Path.Combine(fixture.Root, ".git", "config.lock");
        await File.WriteAllTextAsync(configLockPath, "occupied", TestContext.Current.CancellationToken);
        var releaseLock = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            File.Delete(configLockPath);
        }, TestContext.Current.CancellationToken);

        var result = RunPowerShell(
            fixture.RepositoryInstallScript,
            "-RepositoryRoot",
            fixture.Root);
        await releaseLock;

        AssertPowerShellSucceeded(result);
        Assert.Equal(".githooks", GitRunner.Run(fixture.Root, "config", "--get", "core.hooksPath").Trim());
    }

    [Fact]
    public void Pre_commit_skips_guard_for_non_release_changes()
    {
        using var fixture = GitHookFixture.Create();
        fixture.Stage("docs/note.md", "documentation");

        var result = RunPowerShell(
            fixture.RepositoryPreCommitScript,
            "-RepositoryRoot",
            fixture.Root);

        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("src/A/A.cs")]
    [InlineData("scripts/hub/New-Package.ps1")]
    [InlineData(".github/workflows/publish-hub.yml")]
    [InlineData(".github/workflows/tests-linux.yml")]
    public void Pre_commit_propagates_guard_failure_for_release_changes(string relativePath)
    {
        using var fixture = GitHookFixture.Create();
        fixture.Stage(relativePath, "release input");

        var result = RunPowerShell(
            fixture.RepositoryPreCommitScript,
            "-RepositoryRoot",
            fixture.Root);

        Assert.Equal(GitHookFixture.GuardFailureExitCode, result.ExitCode);
        Assert.Contains("Checking release version guards before commit", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_test_script_uses_isolated_artifacts()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var testScript = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "test.ps1"));
        var normalized = testScript.Replace('\\', '/');

        Assert.Contains("tests/Tests.slnx", normalized, StringComparison.Ordinal);
        Assert.Contains("--artifacts-path", normalized, StringComparison.Ordinal);
        Assert.Contains("artifacts/test", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("--no-restore", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_test_solution_contains_every_test_project()
    {
        var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
        var testsRoot = Path.Combine(repositoryRoot, "tests");
        var expected = Directory
            .GetFiles(testsRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(testsRoot, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actual = System.Xml.Linq.XDocument
            .Load(Path.Combine(testsRoot, "Tests.slnx"))
            .Descendants("Project")
            .Select(element => (string?)element.Attribute("Path"))
            .Where(static path => path is not null)
            .Select(static path => path!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Pre_push_runs_repository_tests_before_local_feed_e2e_and_propagates_e2e_failure()
    {
        using var fixture = GitHookFixture.Create();

        var result = RunPowerShell(
            fixture.RepositoryPrePushScript,
            "-RepositoryRoot",
            fixture.Root);

        Assert.Equal(GitHookFixture.E2EFailureExitCode, result.ExitCode);
        Assert.Contains("Repository tests invoked", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Running required local package E2E before push", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("LocalFeed E2E invoked", result.StandardOutput, StringComparison.Ordinal);
        Assert.True(
            result.StandardOutput.IndexOf("Repository tests invoked", StringComparison.Ordinal) <
            result.StandardOutput.IndexOf("LocalFeed E2E invoked", StringComparison.Ordinal));
    }

    [Fact]
    public void Pre_push_propagates_repository_test_failure_without_running_e2e()
    {
        using var fixture = GitHookFixture.Create(GitHookFixture.RepositoryTestsFailureExitCode);

        var result = RunPowerShell(
            fixture.RepositoryPrePushScript,
            "-RepositoryRoot",
            fixture.Root);

        Assert.Equal(GitHookFixture.RepositoryTestsFailureExitCode, result.ExitCode);
        Assert.Contains("Repository tests invoked", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalFeed E2E invoked", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Pre_push_reuses_successful_validation_for_the_same_clean_head_and_toolchain()
    {
        using var fixture = GitHookFixture.Create(e2eExitCode: 0);
        fixture.CommitAll();

        var first = RunPowerShell(
            fixture.RepositoryPrePushScript,
            "-RepositoryRoot",
            fixture.Root);
        var second = RunPowerShell(
            fixture.RepositoryPrePushScript,
            "-RepositoryRoot",
            fixture.Root);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("Repository tests invoked", first.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("LocalFeed E2E invoked", first.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(0, second.ExitCode);
        Assert.Contains("Reusing repository test result", second.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Reusing local package E2E result", second.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Repository tests invoked", second.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalFeed E2E invoked", second.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Pre_push_reuses_tests_but_retries_a_failed_e2e_for_the_same_clean_head()
    {
        using var fixture = GitHookFixture.Create();
        fixture.CommitAll();

        var first = RunPowerShell(
            fixture.RepositoryPrePushScript,
            "-RepositoryRoot",
            fixture.Root);
        var second = RunPowerShell(
            fixture.RepositoryPrePushScript,
            "-RepositoryRoot",
            fixture.Root);

        Assert.Equal(GitHookFixture.E2EFailureExitCode, first.ExitCode);
        Assert.Contains("Repository tests invoked", first.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(GitHookFixture.E2EFailureExitCode, second.ExitCode);
        Assert.Contains("Reusing repository test result", second.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Repository tests invoked", second.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("LocalFeed E2E invoked", second.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Reusing local package E2E result", second.StandardOutput, StringComparison.Ordinal);
    }

    private static ProcessResult RunPowerShell(string script, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start pwsh.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static void AssertPowerShellSucceeded(ProcessResult result) =>
        Assert.True(
            result.ExitCode == 0,
            $"PowerShell exited with {result.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{result.StandardError}");

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class GitHookFixture : IDisposable
    {
        public const int GuardFailureExitCode = 23;
        public const int E2EFailureExitCode = 24;
        public const int RepositoryTestsFailureExitCode = 25;

        private GitHookFixture(string root, string repositoryRoot)
        {
            Root = root;
            RepositoryInstallScript = Path.Combine(repositoryRoot, "scripts", "git", "install-hooks.ps1");
            RepositoryPreCommitScript = Path.Combine(repositoryRoot, "scripts", "git", "pre-commit.ps1");
            RepositoryPrePushScript = Path.Combine(root, "scripts", "git", "pre-push.ps1");
        }

        public string Root { get; }

        public string RepositoryInstallScript { get; }

        public string RepositoryPreCommitScript { get; }

        public string RepositoryPrePushScript { get; }

        public static GitHookFixture Create(
            int repositoryTestExitCode = 0,
            int e2eExitCode = E2EFailureExitCode)
        {
            var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
            var root = Path.Combine(Path.GetTempPath(), "lakona-git-hook-fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            GitRunner.Run(root, "init");

            var hookSource = Path.Combine(repositoryRoot, ".githooks", "pre-commit");
            var hookTarget = Path.Combine(root, ".githooks", "pre-commit");
            Directory.CreateDirectory(Path.GetDirectoryName(hookTarget)!);
            File.Copy(hookSource, hookTarget);

            var prePushHookSource = Path.Combine(repositoryRoot, ".githooks", "pre-push");
            var prePushHookTarget = Path.Combine(root, ".githooks", "pre-push");
            File.Copy(prePushHookSource, prePushHookTarget);

            var prePushScriptSource = Path.Combine(repositoryRoot, "scripts", "git", "pre-push.ps1");
            var prePushScriptTarget = Path.Combine(root, "scripts", "git", "pre-push.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(prePushScriptTarget)!);
            File.Copy(prePushScriptSource, prePushScriptTarget);

            var repositoryTestScript = Path.Combine(root, "scripts", "test.ps1");
            File.WriteAllText(
                repositoryTestScript,
                $$"""
                param([string] $RepositoryRoot)
                Write-Host "Repository tests invoked"
                exit {{repositoryTestExitCode}}
                """);

            var e2eScript = Path.Combine(root, ".agents", "skills", "lakona-e2e-testing", "scripts", "run-e2e.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(e2eScript)!);
            File.WriteAllText(
                e2eScript,
                $$"""
                param([string] $Feed)
                if ($Feed -ne "LocalFeed") { exit {{E2EFailureExitCode + 1}} }
                Write-Host "LocalFeed E2E invoked"
                exit {{e2eExitCode}}
                """);

            var guardScript = Path.Combine(root, "scripts", "check-release-version-guards.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(guardScript)!);
            File.WriteAllText(guardScript, $"exit {GuardFailureExitCode}{Environment.NewLine}");

            return new GitHookFixture(root, repositoryRoot);
        }

        public void CommitAll()
        {
            GitRunner.Run(Root, "config", "user.name", "Lakona Tests");
            GitRunner.Run(Root, "config", "user.email", "lakona-tests@example.invalid");
            GitRunner.Run(Root, "add", ".");
            GitRunner.Run(Root, "commit", "-m", "Fixture");
        }

        public void Stage(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            GitRunner.Run(Root, "add", relativePath);
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
                return;

            foreach (var path in Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(path, FileAttributes.Normal);

            Directory.Delete(Root, recursive: true);
        }
    }
}
