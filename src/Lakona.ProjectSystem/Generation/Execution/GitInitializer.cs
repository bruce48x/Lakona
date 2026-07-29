using System.Runtime.InteropServices;

namespace Lakona.ProjectSystem.Generation.Execution;

internal sealed class GitInitializer(IGitCommandRunner runner)
{
    public async Task<GitInitializationResult> InitializeAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        // Step 1: Check Git availability
        GitCommandResult versionResult;
        try
        {
            versionResult = await runner.RunAsync(projectRoot, ["--version"], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new GitInitializationResult(GitInitializationStatus.SkippedGitUnavailable);
        }

        if (versionResult.ExitCode != 0)
        {
            return new GitInitializationResult(GitInitializationStatus.SkippedGitUnavailable);
        }

        // Step 2: Detect worktree status
        var revParseResult = await runner.RunAsync(projectRoot, ["rev-parse", "--show-toplevel"], cancellationToken)
            .ConfigureAwait(false);

        if (revParseResult.ExitCode == 0)
        {
            var topLevel = revParseResult.StdOut.Trim();
            if (PathsEqual(topLevel, projectRoot))
            {
                // Already a Git repo rooted here — check for existing commits
                var headResult = await runner.RunAsync(projectRoot, ["rev-parse", "--verify", "HEAD"], cancellationToken)
                    .ConfigureAwait(false);
                if (headResult.ExitCode == 0)
                {
                    return new GitInitializationResult(GitInitializationStatus.SkippedAlreadyCommitted);
                }

                // Empty repo — rename branch to main
                var renameResult = await runner.RunAsync(projectRoot, ["branch", "-M", "main"], cancellationToken)
                    .ConfigureAwait(false);
                if (renameResult.ExitCode != 0)
                {
                    return new GitInitializationResult(
                        GitInitializationStatus.InitializationFailed,
                        "unable to set main branch");
                }
            }
            else
            {
                // Inside a parent worktree
                return new GitInitializationResult(GitInitializationStatus.SkippedParentWorktree);
            }
        }
        else
        {
            // Not in any worktree — initialize
            var initResult = await runner.RunAsync(projectRoot, ["init", "-b", "main"], cancellationToken)
                .ConfigureAwait(false);
            if (initResult.ExitCode != 0)
            {
                // Fallback: older Git without -b support
                var initFallback = await runner.RunAsync(projectRoot, ["init"], cancellationToken)
                    .ConfigureAwait(false);
                if (initFallback.ExitCode != 0)
                {
                    return new GitInitializationResult(
                        GitInitializationStatus.InitializationFailed,
                        initFallback.StdErr.Length > 0 ? initFallback.StdErr : "git init failed");
                }

                var branchResult = await runner.RunAsync(projectRoot, ["branch", "-M", "main"], cancellationToken)
                    .ConfigureAwait(false);
                if (branchResult.ExitCode != 0)
                {
                    return new GitInitializationResult(
                        GitInitializationStatus.InitializationFailed,
                        "unable to set main branch");
                }
            }
        }

        // Step 3: Identity check
        var userNameResult = await runner.RunAsync(projectRoot, ["config", "user.name"], cancellationToken)
            .ConfigureAwait(false);
        var userEmailResult = await runner.RunAsync(projectRoot, ["config", "user.email"], cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(userNameResult.StdOut) ||
            string.IsNullOrWhiteSpace(userEmailResult.StdOut))
        {
            return new GitInitializationResult(GitInitializationStatus.InitializedNoCommitMissingIdentity);
        }

        // Step 4: Add and commit
        var addResult = await runner.RunAsync(projectRoot, ["add", "-A"], cancellationToken)
            .ConfigureAwait(false);
        if (addResult.ExitCode != 0)
        {
            return new GitInitializationResult(
                GitInitializationStatus.InitializationFailed,
                addResult.StdErr.Length > 0 ? addResult.StdErr : "git add failed");
        }

        var statusResult = await runner.RunAsync(projectRoot, ["status", "--porcelain"], cancellationToken)
            .ConfigureAwait(false);
        if (statusResult.ExitCode != 0)
        {
            return new GitInitializationResult(
                GitInitializationStatus.CommitFailed,
                statusResult.StdErr.Length > 0 ? statusResult.StdErr : "git status failed");
        }

        if (string.IsNullOrWhiteSpace(statusResult.StdOut))
        {
            return new GitInitializationResult(GitInitializationStatus.InitializedNoCommitNoFiles);
        }

        var commitResult = await runner.RunAsync(
                projectRoot,
                ["commit", "-m", "Initial Lakona project"],
                cancellationToken)
            .ConfigureAwait(false);
        if (commitResult.ExitCode != 0)
        {
            return new GitInitializationResult(
                GitInitializationStatus.CommitFailed,
                commitResult.StdErr.Length > 0 ? commitResult.StdErr : "commit failed");
        }

        return new GitInitializationResult(GitInitializationStatus.InitializedAndCommitted);
    }

    private static bool PathsEqual(string a, string b)
    {
        var normalizedA = NormalizePath(a);
        var normalizedB = NormalizePath(b);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(normalizedA, normalizedB, StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
