namespace Lakona.ProjectSystem;

public enum LakonaGitInitializationStatus
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

public sealed record LakonaProjectCreationResult(
    string RootPath,
    LakonaGitInitializationStatus GitStatus,
    string? GitReason = null);

public sealed class LakonaProjectCreationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
