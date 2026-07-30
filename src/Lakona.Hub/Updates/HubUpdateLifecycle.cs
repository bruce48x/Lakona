namespace Lakona.Hub.Updates;

internal sealed class HubUpdateLifecycle(IHubUpdateService updateService)
{
    private readonly SemaphoreSlim checkGate = new(1, 1);
    private bool checkInProgress;
    private bool wasDeactivated;

    public HubAvailableUpdate? AvailableUpdate { get; private set; }

    public bool HasChecked { get; private set; }

    public bool IsChecking => checkInProgress;

    public bool NeedsReactivationCheck => wasDeactivated;

    public DateTimeOffset? CheckedAtUtc { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken);

    public void Deactivate()
    {
        wasDeactivated = true;
    }

    public Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        if (!wasDeactivated)
        {
            return Task.CompletedTask;
        }

        wasDeactivated = false;
        return RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await checkGate.WaitAsync(cancellationToken);
        checkInProgress = true;
        try
        {
            AvailableUpdate = await updateService.CheckAsync(cancellationToken);
            HasChecked = true;
            CheckedAtUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            checkInProgress = false;
            checkGate.Release();
        }
    }
}
