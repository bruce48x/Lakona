namespace Lakona.Tool.Execution;

internal enum GitInitializationStatus
{
    InitializedAndCommitted,
    InitializedNoCommitMissingIdentity,
    InitializedNoCommitNoFiles,
    SkippedParentWorktree,
    SkippedAlreadyCommitted,
    SkippedGitUnavailable,
    InitializationFailed,
    CommitFailed
}

internal sealed record GitInitializationResult(
    GitInitializationStatus Status,
    string? Reason = null);
