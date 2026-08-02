namespace Lakona.Hub.Updates;

internal sealed class HubUpdateLifecycle
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(1);
    private readonly IHubUpdateService updateService;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim checkGate = new(1, 1);
    private bool checkInProgress;
    private bool wasDeactivated;

    public HubUpdateLifecycle(IHubUpdateService updateService, TimeProvider? timeProvider = null)
    {
        this.updateService = updateService;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public HubAvailableUpdate? AvailableUpdate { get; private set; }

    public bool HasChecked { get; private set; }

    public bool IsChecking => checkInProgress;

    public bool NeedsReactivationCheck => wasDeactivated;

    public DateTimeOffset? CheckedAtUtc { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        RefreshIfStaleAsync(cancellationToken);

    public void Restore(HubAvailableUpdate? availableUpdate, DateTimeOffset checkedAtUtc)
    {
        AvailableUpdate = availableUpdate;
        HasChecked = true;
        CheckedAtUtc = checkedAtUtc;
    }

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
        return RefreshIfStaleAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(force: true, cancellationToken);

    private Task RefreshIfStaleAsync(CancellationToken cancellationToken) =>
        RefreshCoreAsync(force: false, cancellationToken);

    private async Task RefreshCoreAsync(bool force, CancellationToken cancellationToken)
    {
        await checkGate.WaitAsync(cancellationToken);
        try
        {
            if (!force && IsAutomaticCheckFresh())
            {
                return;
            }

            checkInProgress = true;
            try
            {
                AvailableUpdate = await updateService.CheckAsync(cancellationToken);
                HasChecked = true;
                CheckedAtUtc = timeProvider.GetUtcNow();
            }
            finally
            {
                checkInProgress = false;
            }
        }
        finally
        {
            checkGate.Release();
        }
    }

    private bool IsAutomaticCheckFresh()
    {
        if (!HasChecked || CheckedAtUtc is not { } checkedAtUtc)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        return checkedAtUtc <= now && now - checkedAtUtc < AutomaticCheckInterval;
    }
}
