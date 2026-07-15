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

        Assert.Equal(0, result.ExitCode);
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class GitHookFixture : IDisposable
    {
        public const int GuardFailureExitCode = 23;

        private GitHookFixture(string root, string repositoryRoot)
        {
            Root = root;
            RepositoryInstallScript = Path.Combine(repositoryRoot, "scripts", "git", "install-hooks.ps1");
            RepositoryPreCommitScript = Path.Combine(repositoryRoot, "scripts", "git", "pre-commit.ps1");
        }

        public string Root { get; }

        public string RepositoryInstallScript { get; }

        public string RepositoryPreCommitScript { get; }

        public static GitHookFixture Create()
        {
            var repositoryRoot = GitChangeSetReader.FindRepositoryRoot();
            var root = Path.Combine(Path.GetTempPath(), "lakona-git-hook-fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            GitRunner.Run(root, "init");

            var hookSource = Path.Combine(repositoryRoot, ".githooks", "pre-commit");
            var hookTarget = Path.Combine(root, ".githooks", "pre-commit");
            Directory.CreateDirectory(Path.GetDirectoryName(hookTarget)!);
            File.Copy(hookSource, hookTarget);

            var guardScript = Path.Combine(root, "scripts", "check-release-version-guards.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(guardScript)!);
            File.WriteAllText(guardScript, $"exit {GuardFailureExitCode}{Environment.NewLine}");

            return new GitHookFixture(root, repositoryRoot);
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
