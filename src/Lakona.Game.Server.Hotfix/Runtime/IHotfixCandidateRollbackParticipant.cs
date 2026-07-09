namespace Lakona.Game.Server.Hotfix;

public interface IHotfixCandidateRollbackParticipant
{
    ValueTask<IHotfixCandidateRollbackHandle> BeginCandidateStartupAsync(
        string candidateName,
        IServiceProvider services,
        CancellationToken cancellationToken = default);
}

public interface IHotfixCandidateRollbackHandle : IAsyncDisposable
{
    ValueTask CommitAsync(CancellationToken cancellationToken = default);

    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

public interface IHotfixCandidateRollbackActivationHandle
{
    IDisposable ActivateCandidateRollback();
}
