using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixActorTickHostedService(
    IHotfixManager hotfix,
    HotfixActorTickScheduler scheduler,
    LakonaGameRuntimeOptions runtimeOptions) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ApplySnapshotAsync(FilterSnapshot(hotfix.Current), cancellationToken).ConfigureAwait(false);
        hotfix.Reloaded += OnReloaded;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        hotfix.Reloaded -= OnReloaded;
        await scheduler.DisposeAsync().ConfigureAwait(false);
    }

    private void OnReloaded(object? sender, HotfixReloadResult result)
    {
        if (result.Succeeded)
        {
            ApplySnapshotAsync(FilterSnapshot(result.Current), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
    }

    private Task ApplySnapshotAsync(HotfixSnapshot snapshot, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        scheduler.Apply(snapshot);
        return Task.CompletedTask;
    }

    private HotfixSnapshot FilterSnapshot(HotfixSnapshot snapshot)
    {
        if (runtimeOptions.Feature is null)
        {
            return snapshot;
        }

        var allowed = new HashSet<string>(runtimeOptions.Feature, StringComparer.OrdinalIgnoreCase);
        var features = snapshot.Features
            .Where(feature => allowed.Contains(feature.Name))
            .ToArray();

        if (features.Length == snapshot.Features.Count)
        {
            return snapshot;
        }

        return new HotfixSnapshot(
            snapshot.Version,
            snapshot.SourceKind,
            snapshot.SourcePath,
            snapshot.LoadedAtUtc,
            snapshot.DispatchTableVersion,
            snapshot.Methods,
            snapshot.LastReloadStatus,
            snapshot.LastFailureMessage,
            snapshot.LastFailureExceptionType,
            features);
    }
}
