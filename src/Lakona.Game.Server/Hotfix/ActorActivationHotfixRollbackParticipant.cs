using Lakona.Game.Server.Actors;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hotfix;

internal sealed class ActorActivationHotfixRollbackParticipant(
    ActorActivationCatalog activationCatalog,
    ActorActivationRollbackRecorder rollbackRecorder,
    ILogger<ActorActivationHotfixRollbackParticipant>? logger = null) : IHotfixCandidateRollbackParticipant
{
    public ValueTask<IHotfixCandidateRollbackHandle> BeginCandidateStartupAsync(
        string candidateName,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var scope = rollbackRecorder.CreateScope();
        return new ValueTask<IHotfixCandidateRollbackHandle>(new Handle(activationCatalog, scope, logger));
    }

    private sealed class Handle(
        ActorActivationCatalog activationCatalog,
        ActorActivationRollbackRecorder.Scope scope,
        ILogger? logger) : IHotfixCandidateRollbackHandle, IHotfixCandidateRollbackActivationHandle
    {
        private bool disposed;

        public IDisposable ActivateCandidateRollback() => scope.Activate();

        public ValueTask CommitAsync(CancellationToken cancellationToken = default) => default;

        public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            var records = scope.Created.ToArray();
            for (var index = records.Length - 1; index >= 0; index--)
            {
                var record = records[index];
                try
                {
                    await activationCatalog.DestroyAsync(
                        record.ActorType,
                        record.ActorId,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    logger?.LogWarning(
                        exception,
                        "Failed to roll back hotfix-created actor {ActorId} of type {ActorType}.",
                        record.ActorId.Value,
                        record.ActorType.FullName);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
