using System.Reflection;
using Lakona.Game.Server.Actors;
using Lakona.Game.Server.Hotfix;
using Microsoft.Extensions.Logging;

namespace Lakona.Game.Server.Hotfix;

internal sealed class ActorHostingHotfixRollbackParticipant(
    ActorHosting actorHosting,
    ActorHostingRollbackRecorder rollbackRecorder,
    ILogger<ActorHostingHotfixRollbackParticipant>? logger = null) : IHotfixCandidateRollbackParticipant
{
    public ValueTask<IHotfixCandidateRollbackHandle> BeginCandidateFeatureStartAsync(
        string featureName,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var scope = rollbackRecorder.BeginScope();
        return new ValueTask<IHotfixCandidateRollbackHandle>(new Handle(actorHosting, scope, logger));
    }

    private sealed class Handle(
        ActorHosting actorHosting,
        ActorHostingRollbackRecorder.Scope scope,
        ILogger? logger) : IHotfixCandidateRollbackHandle
    {
        private static readonly MethodInfo DestroyMethod = typeof(ActorHosting)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == nameof(ActorHosting.DestroyAsync) && method.IsGenericMethodDefinition);

        private bool _disposed;

        public async ValueTask CommitAsync(CancellationToken cancellationToken = default)
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }

        public async ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            var records = scope.Created.ToArray();
            for (var index = records.Length - 1; index >= 0; index--)
            {
                var record = records[index];
                try
                {
                    var task = (ValueTask)DestroyMethod
                        .MakeGenericMethod(record.ActorType)
                        .Invoke(actorHosting, [record.ActorId, cancellationToken])!;
                    await task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(
                        ex,
                        "Failed to roll back hotfix-created actor {ActorId} of type {ActorType}.",
                        record.ActorId.Value,
                        record.ActorType.FullName);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
