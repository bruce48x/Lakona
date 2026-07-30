using Lakona.Hub.Updates;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubUpdateLifecycleTests
{
    [Fact]
    public async Task StartupAndReturningToHubEachCheckForUpdates()
    {
        var service = new RecordingUpdateService();
        var lifecycle = new HubUpdateLifecycle(service);

        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        lifecycle.Deactivate();
        await lifecycle.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, service.CheckCount);
    }

    [Fact]
    public async Task ReturningToHubDuringACheckQueuesAnotherCheck()
    {
        var service = new PausingUpdateService();
        var lifecycle = new HubUpdateLifecycle(service);

        var startupCheck = lifecycle.StartAsync(TestContext.Current.CancellationToken);
        await service.FirstCheckStarted.Task;
        lifecycle.Deactivate();
        var returnCheck = lifecycle.ActivateAsync(TestContext.Current.CancellationToken);
        service.CompleteFirstCheck();
        await Task.WhenAll(startupCheck, returnCheck);

        Assert.Equal(2, service.CheckCount);
    }

    [Fact]
    public async Task ActivationWithoutLeavingHubDoesNotCheckAgain()
    {
        var service = new RecordingUpdateService();
        var lifecycle = new HubUpdateLifecycle(service);

        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        await lifecycle.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, service.CheckCount);
    }

    private sealed class RecordingUpdateService : IHubUpdateService
    {
        public int CheckCount { get; private set; }

        public string CurrentVersion => "1.0.0";

        public Task<HubAvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return Task.FromResult<HubAvailableUpdate?>(null);
        }

        public Task<HubUpdateLaunchResult> PrepareAndLaunchAsync(
            HubAvailableUpdate update,
            IProgress<HubUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class PausingUpdateService : IHubUpdateService
    {
        private readonly TaskCompletionSource firstCheckCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstCheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CheckCount { get; private set; }

        public string CurrentVersion => "1.0.0";

        public async Task<HubAvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;
            if (CheckCount == 1)
            {
                FirstCheckStarted.SetResult();
                await firstCheckCompletion.Task.WaitAsync(cancellationToken);
            }

            return null;
        }

        public void CompleteFirstCheck() => firstCheckCompletion.SetResult();

        public Task<HubUpdateLaunchResult> PrepareAndLaunchAsync(
            HubAvailableUpdate update,
            IProgress<HubUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
