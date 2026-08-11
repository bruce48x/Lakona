using Lakona.Hub.Updates;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubUpdateWorkflowTests
{
    [Fact]
    public async Task ReturningWithinOneHourDoesNotCheckAgain()
    {
        var service = new RecordingUpdateService();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero));
        using var workflow = Workflow(service, timeProvider: timeProvider);

        await workflow.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromMinutes(59));
        workflow.Deactivate();
        await workflow.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, service.CheckCount);
    }

    [Fact]
    public async Task ReturningAfterOneHourChecksAgain()
    {
        var service = new RecordingUpdateService();
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero));
        using var workflow = Workflow(service, timeProvider: timeProvider);

        await workflow.StartAsync(TestContext.Current.CancellationToken);
        timeProvider.Advance(TimeSpan.FromHours(1));
        workflow.Deactivate();
        await workflow.ActivateAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, service.CheckCount);
    }

    [Fact]
    public async Task ReturningDuringCheckDoesNotQueueAnotherCheck()
    {
        var service = new PausingUpdateService();
        using var workflow = Workflow(service);

        var startupCheck = workflow.StartAsync(TestContext.Current.CancellationToken);
        await service.FirstCheckStarted.Task;
        workflow.Deactivate();
        var returnCheck = workflow.ActivateAsync(TestContext.Current.CancellationToken);
        service.CompleteFirstCheck();
        await Task.WhenAll(startupCheck, returnCheck);

        Assert.Equal(1, service.CheckCount);
    }

    [Fact]
    public async Task RestoredRecentCheckSkipsStartupAndKeepsAvailableUpdate()
    {
        var service = new RecordingUpdateService();
        var now = new DateTimeOffset(2026, 8, 2, 8, 0, 0, TimeSpan.Zero);
        var restored = Settings(now - TimeSpan.FromMinutes(30));
        using var workflow = Workflow(service, restored, new ManualTimeProvider(now));

        await workflow.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, service.CheckCount);
        Assert.True(workflow.IsUpdateAvailable);
        Assert.Contains("1.1.0", workflow.StatusText, StringComparison.Ordinal);
        Assert.Equal("Download & install", workflow.ActionText);
        Assert.Equal(restored, workflow.Capture());
    }

    [Fact]
    public async Task ExplicitActionChecksEvenWhenLastCheckIsRecent()
    {
        var service = new RecordingUpdateService();
        using var workflow = Workflow(service);

        await workflow.StartAsync(TestContext.Current.CancellationToken);
        var outcome = await workflow.ExecutePrimaryActionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HubUpdateActionOutcome.Checked, outcome);
        Assert.Equal(2, service.CheckCount);
    }

    [Fact]
    public async Task AvailableUpdateActionOwnsProgressAndReturnsRestartOutcome()
    {
        var service = new RecordingUpdateService
        {
            LaunchResult = HubUpdateLaunchResult.ApplicationRestartInitiated
        };
        using var workflow = Workflow(service, Settings(DateTimeOffset.UtcNow));

        var outcome = await workflow.ExecutePrimaryActionAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HubUpdateActionOutcome.ApplicationRestartInitiated, outcome);
        Assert.Equal(1, service.InstallCount);
        Assert.False(workflow.IsProgressVisible);
        Assert.True(workflow.CanExecute);
        Assert.Contains("installed successfully", workflow.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompletedStatusUsesCurrentLanguageInsteadOfCachedDisplayText()
    {
        var service = new RecordingUpdateService
        {
            LaunchResult = HubUpdateLaunchResult.ApplicationRestartInitiated
        };
        var localization = new HubLocalization(HubLanguage.English);
        using var workflow = new HubUpdateWorkflow(
            service,
            localization,
            Settings(DateTimeOffset.UtcNow));

        await workflow.ExecutePrimaryActionAsync(TestContext.Current.CancellationToken);
        var englishStatus = workflow.StatusText;
        localization.SetLanguage(HubLanguage.SimplifiedChinese);

        Assert.NotEqual(englishStatus, workflow.StatusText);
        Assert.Equal(localization.Text.SystemPackageInstalled, workflow.StatusText);
    }

    [Fact]
    public async Task FailedCheckKeepsWorkflowRetryableWithoutPersistingFailure()
    {
        var service = new RecordingUpdateService
        {
            CheckError = new IOException("feed unavailable")
        };
        using var workflow = Workflow(service);
        var persistenceChanges = 0;
        workflow.PersistentStateChanged += (_, _) => persistenceChanges++;

        await workflow.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(workflow.CanExecute);
        Assert.Contains("feed unavailable", workflow.StatusText, StringComparison.Ordinal);
        Assert.Null(workflow.Capture());
        Assert.Equal(0, persistenceChanges);
    }

    private static HubUpdateWorkflow Workflow(
        IHubUpdateService service,
        HubUpdateCheckSettings? restored = null,
        TimeProvider? timeProvider = null) =>
        new(service, new HubLocalization(HubLanguage.English), restored, timeProvider);

    private static HubUpdateCheckSettings Settings(DateTimeOffset checkedAt) => new(
        checkedAt,
        "1.1.0",
        "win-x64",
        "hub-v1.1.0",
        "Lakona.Hub.msi",
        new string('a', 64),
        123);

    private sealed class RecordingUpdateService : IHubUpdateService
    {
        public int CheckCount { get; private set; }

        public int InstallCount { get; private set; }

        public Exception? CheckError { get; init; }

        public HubUpdateLaunchResult LaunchResult { get; init; } = HubUpdateLaunchResult.InstallerOpened;

        public string CurrentVersion => "1.0.0";

        public Task<HubAvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCount++;
            return CheckError is null
                ? Task.FromResult<HubAvailableUpdate?>(null)
                : Task.FromException<HubAvailableUpdate?>(CheckError);
        }

        public Task<HubUpdateLaunchResult> PrepareAndLaunchAsync(
            HubAvailableUpdate update,
            IProgress<HubUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallCount++;
            progress?.Report(new HubUpdateProgress(
                HubUpdateStage.Downloading,
                update.Asset.Size,
                update.Asset.Size));
            progress?.Report(new HubUpdateProgress(HubUpdateStage.Verifying, 0, 0));
            progress?.Report(new HubUpdateProgress(HubUpdateStage.Installing, 0, 0));
            return Task.FromResult(LaunchResult);
        }
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
            FirstCheckStarted.TrySetResult();
            await firstCheckCompletion.Task.WaitAsync(cancellationToken);
            return null;
        }

        public void CompleteFirstCheck() => firstCheckCompletion.TrySetResult();

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
