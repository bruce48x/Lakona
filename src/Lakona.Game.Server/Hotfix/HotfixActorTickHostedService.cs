using Lakona.Game.Server.Hotfix.Abstractions;
using Microsoft.Extensions.Hosting;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixActorTickHostedService(
    IHotfixManager hotfix,
    HotfixActorTickScheduler scheduler) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        scheduler.Apply(hotfix.Current);
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
            scheduler.Apply(result.Current);
        }
    }
}
