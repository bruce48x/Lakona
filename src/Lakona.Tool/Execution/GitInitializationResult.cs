namespace Lakona.Tool.Execution;

internal enum GitInitializationStatus
{
    InitializedAndCommitted,
    InitializedNoCommit,
    SkippedParentWorktree,
    SkippedAlreadyCommitted,
    SkippedGitUnavailable,
    InitializationFailed,
    CommitFailed
}

internal sealed record GitInitializationResult(
    GitInitializationStatus Status,
    string? Reason = null);
