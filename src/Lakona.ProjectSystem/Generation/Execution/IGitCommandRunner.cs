namespace Lakona.ProjectSystem.Generation.Execution;

internal interface IGitCommandRunner
{
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken);
}

internal sealed record GitCommandResult(int ExitCode, string StdOut, string StdErr);
