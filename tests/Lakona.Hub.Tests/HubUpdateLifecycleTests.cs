using Lakona.Hub.Updates;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubUpdateLifecycleTests
{
    [Fact]
    public async Task ReturningToHubWithinOneHourDoesNotCheckAgain()
    {
        var service = new RecordingUpdateService();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero));
        var lifecycle = new HubUpdateLifecycle(service, timeProvider);

        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(59));
        lifecycle.Deactivate();
        await lifecycle.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, service.CheckCount);
    }

    [Fact]
    public async Task ReturningToHubAfterOneHourChecksAgain()
    {
        var service = new RecordingUpdateService();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero));
        var lifecycle = new HubUpdateLifecycle(service, timeProvider);

        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromHours(1));
        lifecycle.Deactivate();
        await lifecycle.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, service.CheckCount);
    }

    [Fact]
    public async Task ReturningToHubDuringACheckDoesNotQueueAnotherCheck()
    {
        var service = new PausingUpdateService();
        var lifecycle = new HubUpdateLifecycle(service);

        var startupCheck = lifecycle.StartAsync(TestContext.Current.CancellationToken);
        await service.FirstCheckStarted.Task;
        lifecycle.Deactivate();
        var returnCheck = lifecycle.ActivateAsync(TestContext.Current.CancellationToken);
        service.CompleteFirstCheck();
        await Task.WhenAll(startupCheck, returnCheck);

        Assert.Equal(1, service.CheckCount);
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

    [Fact]
    public async Task RestoredRecentCheckSkipsStartupCheck()
    {
        var service = new RecordingUpdateService();
        var now = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero);
        var lifecycle = new HubUpdateLifecycle(service, new ManualTimeProvider(now));
        var update = CreateAvailableUpdate();

        lifecycle.Restore(update, now - TimeSpan.FromMinutes(30));
        await lifecycle.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, service.CheckCount);
        Assert.Same(update, lifecycle.AvailableUpdate);
    }

    [Fact]
    public async Task ManualRefreshChecksEvenWhenTheLastCheckIsRecent()
    {
        var service = new RecordingUpdateService();
        var lifecycle = new HubUpdateLifecycle(service);

        await lifecycle.StartAsync(TestContext.Current.CancellationToken);
        await lifecycle.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, service.CheckCount);
    }

    private static HubAvailableUpdate CreateAvailableUpdate() => new(
        "1.1.0",
        "win-x64",
        "hub-v1.1.0",
        new HubReleaseAsset("Lakona.Hub.msi", new string('a', 64), 123));

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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }
}
