namespace Lakona.Game.Server.Hotfix;

public interface IHotfixCandidateRollbackParticipant
{
    ValueTask<IHotfixCandidateRollbackHandle> BeginCandidateFeatureStartAsync(
        string featureName,
        IServiceProvider services,
        CancellationToken cancellationToken = default);
}

public interface IHotfixCandidateRollbackHandle : IAsyncDisposable
{
    ValueTask CommitAsync(CancellationToken cancellationToken = default);

    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}
