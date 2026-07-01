using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixCandidateRollbackScope : IAsyncDisposable
{
    private readonly List<IHotfixCandidateRollbackHandle> _handles = [];
    private bool _disposed;

    private HotfixCandidateRollbackScope()
    {
    }

    public static async ValueTask<HotfixCandidateRollbackScope> BeginAsync(
        string featureName,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var scope = new HotfixCandidateRollbackScope();
        var participants = services.GetServices<IHotfixCandidateRollbackParticipant>().ToArray();
        try
        {
            foreach (var participant in participants)
            {
                var handle = await participant
                    .BeginCandidateFeatureStartAsync(featureName, services, cancellationToken)
                    .ConfigureAwait(false);
                scope._handles.Add(handle);
            }

            return scope;
        }
        catch
        {
            await scope.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await scope.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        foreach (var handle in _handles)
        {
            await handle.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        for (var index = _handles.Count - 1; index >= 0; index--)
        {
            try
            {
                await _handles[index].RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
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
        for (var index = _handles.Count - 1; index >= 0; index--)
        {
            try
            {
                await _handles[index].DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
