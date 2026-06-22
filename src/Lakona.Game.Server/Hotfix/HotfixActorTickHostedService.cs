using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixActorTickHostedService(
    IHotfixManager hotfix,
    HotfixActorTickScheduler scheduler,
    LakonaGameRuntimeOptions runtimeOptions) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        scheduler.Apply(FilterSnapshot(hotfix.Current));
        hotfix.Reloaded += OnReloaded;
        return Task.CompletedTask;
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
            scheduler.Apply(FilterSnapshot(result.Current));
        }
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
