using Lakona.Hub.Applications;
using Lakona.Hub.Sdk;
using Xunit;

namespace Lakona.Hub.Tests;

public sealed class HubEnvironmentWorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        nameof(HubEnvironmentWorkflowTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsyncPublishesDetectedApplicationsAndSdkIndependently()
    {
        var executable = CreateExecutable("rider.exe");
        using var workflow = Workflow(
            new RecordingSdkManager(ReadySdk()),
            new RecordingApplicationSource([
                new LocalApplicationInstallation(
                    LocalApplicationKind.Rider,
                    "Rider",
                    executable,
                    "2026.1")
            ]));
        var applicationChanges = 0;
        var persistenceChanges = 0;
        workflow.ApplicationsChanged += (_, _) => applicationChanges++;
        workflow.PersistentStateChanged += (_, _) => persistenceChanges++;

        var outcome = await workflow.StartAsync(TestContext.Current.CancellationToken);

        Assert.Null(outcome.ApplicationDetectionError);
        Assert.Null(outcome.SdkInspectionError);
        Assert.Contains(workflow.ApplicationTools, tool =>
            tool.Kind == LocalApplicationKind.Rider && tool.PathText.Contains(executable, StringComparison.Ordinal));
        Assert.Equal(executable, workflow.ServerEditorSelection.SelectedEditor?.ExecutablePath);
        Assert.EndsWith("dotnet.exe", workflow.SdkExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ready", workflow.SdkStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, applicationChanges);
        Assert.Equal(1, persistenceChanges);
    }

    [Fact]
    public async Task ApplicationDetectionFailureDoesNotHideReadySdk()
    {
        using var workflow = Workflow(
            new RecordingSdkManager(ReadySdk()),
            new ThrowingApplicationSource(new IOException("probe failed")));

        var outcome = await workflow.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal("probe failed", outcome.ApplicationDetectionError);
        Assert.Null(outcome.SdkInspectionError);
        Assert.Contains("ready", workflow.SdkStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("detection failed", workflow.EnvironmentSummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallSdkOwnsProgressPublishesReadyStatusAndRejectsDuplicateSubmission()
    {
        var manager = new PausingSdkManager();
        using var workflow = Workflow(manager, new RecordingApplicationSource([]));
        workflow.PrepareSdkInstall();

        var first = workflow.InstallSdkAsync(TestContext.Current.CancellationToken);
        await manager.InstallStarted.Task;
        var duplicate = await workflow.InstallSdkAsync(TestContext.Current.CancellationToken);

        Assert.False(duplicate.Succeeded);
        Assert.False(workflow.CanInstallSdk);
        manager.CompleteInstall();
        var installed = await first;

        Assert.True(installed.Succeeded);
        Assert.Equal("10.0.102", installed.Version);
        Assert.True(workflow.CanInstallSdk);
        Assert.Contains("ready", workflow.SdkStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("SDK installation complete.", workflow.SdkInstallProgressText);
    }

    [Fact]
    public async Task FailedSdkInstallKeepsErrorLocalizedAndAllowsRetry()
    {
        var manager = new RecordingSdkManager(
            ReadySdk(),
            new IOException("archive invalid"));
        using var workflow = Workflow(manager, new RecordingApplicationSource([]));

        var outcome = await workflow.InstallSdkAsync(TestContext.Current.CancellationToken);

        Assert.False(outcome.Succeeded);
        Assert.True(workflow.CanInstallSdk);
        Assert.True(workflow.HasSdkInstallError);
        Assert.Contains("archive invalid", workflow.SdkInstallErrorText, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private HubEnvironmentWorkflow Workflow(
        IHubSdkManager sdkManager,
        IApplicationProbeSource applicationSource) =>
        new(
            new HubLocalization(HubLanguage.English),
            sdkManager,
            new InstalledApplicationCatalog(applicationSource),
            new ManualApplicationStore(Path.Combine(root, "manual-applications.json")),
            null,
            []);

    private string CreateExecutable(string fileName)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string SdkExecutable() => CreateExecutable("dotnet.exe");

    private HubSdkStatus ReadySdk() =>
        new(true, HubSdkSource.System, "10.0.102", SdkExecutable());

    private sealed class RecordingApplicationSource(
        IReadOnlyList<LocalApplicationInstallation> applications) : IApplicationProbeSource
    {
        public IEnumerable<LocalApplicationInstallation> FindApplications() => applications;
    }

    private sealed class ThrowingApplicationSource(Exception error) : IApplicationProbeSource
    {
        public IEnumerable<LocalApplicationInstallation> FindApplications() => throw error;
    }

    private sealed class RecordingSdkManager(
        HubSdkStatus status,
        Exception? installError = null) : IHubSdkManager
    {
        public Task<HubSdkStatus> InspectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(status);

        public Task<HubSdkStatus> InstallAsync(
            IProgress<HubSdkProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (installError is not null)
            {
                return Task.FromException<HubSdkStatus>(installError);
            }

            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Downloading, 100, 100));
            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Completed));
            return Task.FromResult(status);
        }
    }

    private sealed class PausingSdkManager : IHubSdkManager
    {
        private readonly TaskCompletionSource installCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource InstallStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HubSdkStatus> InspectAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HubSdkStatus(false, HubSdkSource.None, null, null));

        public async Task<HubSdkStatus> InstallAsync(
            IProgress<HubSdkProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            InstallStarted.TrySetResult();
            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Downloading, 50, 100));
            await installCompletion.Task.WaitAsync(cancellationToken);
            progress?.Report(new HubSdkProgress(HubSdkInstallStage.Completed));
            return new HubSdkStatus(true, HubSdkSource.Managed, "10.0.102", "dotnet");
        }

        public void CompleteInstall() => installCompletion.TrySetResult();
    }
}
