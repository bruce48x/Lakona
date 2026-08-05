using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lakona.Hub.Updates;

internal enum HubUpdateActionOutcome
{
    NoChange,
    Checked,
    InstallerOpened,
    ApplicationRestartInitiated,
    Failed
}

internal enum HubUpdateStatusKind
{
    None,
    Checking,
    InstallerOpened,
    ApplicationRestartInitiated
}

public sealed class HubUpdateWorkflow : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(1);
    private readonly IHubUpdateService updateService;
    private readonly HubLocalization localization;
    private readonly TimeProvider timeProvider;
    private bool wasDeactivated;
    private bool hasChecked;
    private bool isChecking;
    private bool isInstalling;
    private DateTimeOffset? checkedAtUtc;
    private HubAvailableUpdate? availableUpdate;
    private HubUpdateProgress? progress;
    private string? error;
    private HubUpdateStatusKind statusKind;

    internal HubUpdateWorkflow(
        IHubUpdateService updateService,
        HubLocalization localization,
        HubUpdateCheckSettings? restoredSettings = null,
        TimeProvider? timeProvider = null)
    {
        this.updateService = updateService;
        this.localization = localization;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        Restore(restoredSettings);
        localization.PropertyChanged += Localization_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? PersistentStateChanged;

    public string CurrentVersionText => Text.CurrentHubVersion(updateService.CurrentVersion);

    public string StatusText => error is not null
        ? Text.UpdateFailed(error)
        : progress is not null
            ? ProgressStatusText
            : statusKind switch
            {
                HubUpdateStatusKind.Checking => Text.CheckingForUpdates,
                HubUpdateStatusKind.InstallerOpened => Text.SystemPackageInstallerOpened,
                HubUpdateStatusKind.ApplicationRestartInitiated => Text.SystemPackageInstalled,
                _ => availableUpdate is null
                    ? hasChecked
                        ? Text.NoUpdatesAvailable(updateService.CurrentVersion)
                        : Text.UpdateCheckDescription
                    : Text.SystemPackageUpdateAvailable(availableUpdate.Version)
            };

    public string ActionText => availableUpdate is null
        ? Text.CheckForUpdates
        : Text.DownloadAndInstall;

    public bool IsProjectActionVisible => availableUpdate is not null;

    public string ProjectActionText => availableUpdate is null
        ? string.Empty
        : Text.InstallHubUpdate(availableUpdate.Version);

    public bool CanExecute => !isChecking && !isInstalling;

    public bool IsProgressVisible => progress is not null;

    public bool IsProgressIndeterminate => progress?.Stage != HubUpdateStage.Downloading;

    public double ProgressValue => progress?.Percentage ?? 0;

    public string ProgressText => progress switch
    {
        { Stage: HubUpdateStage.Downloading } value => Text.DownloadProgress(
            value.Percentage,
            FormatByteSize(value.BytesReceived),
            FormatByteSize(value.TotalBytes)),
        { Stage: HubUpdateStage.Verifying } => Text.VerifyingSystemPackage,
        { Stage: HubUpdateStage.LaunchingInstaller } => Text.OpeningSystemPackageInstaller,
        { Stage: HubUpdateStage.Installing } => Text.InstallingSystemPackage,
        _ => string.Empty
    };

    internal Task StartAsync(CancellationToken cancellationToken) =>
        CheckAsync(force: false, cancellationToken);

    internal void Deactivate() => wasDeactivated = true;

    internal Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (!wasDeactivated)
        {
            return Task.CompletedTask;
        }

        wasDeactivated = false;
        return CheckAsync(force: false, cancellationToken);
    }

    internal async Task<HubUpdateActionOutcome> ExecutePrimaryActionAsync(
        CancellationToken cancellationToken)
    {
        if (!CanExecute)
        {
            return HubUpdateActionOutcome.NoChange;
        }

        return availableUpdate is null
            ? await RefreshAsync(cancellationToken)
            : await InstallAsync(availableUpdate, cancellationToken);
    }

    internal HubUpdateCheckSettings? Capture() => !hasChecked
        ? null
        : availableUpdate is null
            ? new HubUpdateCheckSettings(checkedAtUtc ?? timeProvider.GetUtcNow(), null, null, null, null, null, null)
            : new HubUpdateCheckSettings(
                checkedAtUtc ?? timeProvider.GetUtcNow(),
                availableUpdate.Version,
                availableUpdate.Platform,
                availableUpdate.Tag,
                availableUpdate.Asset.AssetName,
                availableUpdate.Asset.Sha256,
                availableUpdate.Asset.Size);

    public void Dispose() => localization.PropertyChanged -= Localization_PropertyChanged;

    private HubText Text => localization.Text;

    private async Task<HubUpdateActionOutcome> RefreshAsync(CancellationToken cancellationToken)
    {
        await CheckAsync(force: true, cancellationToken);
        return error is null ? HubUpdateActionOutcome.Checked : HubUpdateActionOutcome.Failed;
    }

    private async Task CheckAsync(bool force, CancellationToken cancellationToken)
    {
        if (isChecking || isInstalling || (!force && IsAutomaticCheckFresh()))
        {
            return;
        }

        isChecking = true;
        error = null;
        statusKind = HubUpdateStatusKind.Checking;
        NotifyStateChanged();
        try
        {
            availableUpdate = await updateService.CheckAsync(cancellationToken);
            hasChecked = true;
            checkedAtUtc = timeProvider.GetUtcNow();
            statusKind = HubUpdateStatusKind.None;
            PersistentStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            statusKind = HubUpdateStatusKind.None;
        }
        finally
        {
            isChecking = false;
            NotifyStateChanged();
        }
    }

    private async Task<HubUpdateActionOutcome> InstallAsync(
        HubAvailableUpdate update,
        CancellationToken cancellationToken)
    {
        isInstalling = true;
        error = null;
        statusKind = HubUpdateStatusKind.None;
        progress = new HubUpdateProgress(HubUpdateStage.Downloading, 0, update.Asset.Size);
        NotifyStateChanged();
        try
        {
            var reporter = new CallbackProgress<HubUpdateProgress>(UpdateProgress);
            var result = await updateService.PrepareAndLaunchAsync(update, reporter, cancellationToken);
            progress = null;
            if (result == HubUpdateLaunchResult.ApplicationRestartInitiated)
            {
                statusKind = HubUpdateStatusKind.ApplicationRestartInitiated;
                return HubUpdateActionOutcome.ApplicationRestartInitiated;
            }

            statusKind = HubUpdateStatusKind.InstallerOpened;
            return HubUpdateActionOutcome.InstallerOpened;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress = null;
            throw;
        }
        catch (Exception exception)
        {
            progress = null;
            error = exception.Message;
            return HubUpdateActionOutcome.Failed;
        }
        finally
        {
            isInstalling = false;
            NotifyStateChanged();
        }
    }

    private void Restore(HubUpdateCheckSettings? settings)
    {
        if (settings is null)
        {
            return;
        }

        hasChecked = true;
        checkedAtUtc = settings.CheckedAtUtc;
        if (settings is { Version: { } version, Platform: { } platform, Tag: { } tag,
                          AssetName: { } assetName, Sha256: { } sha256, Size: { } size } &&
            Version.TryParse(version, out var availableVersion) &&
            Version.TryParse(updateService.CurrentVersion, out var currentVersion) &&
            availableVersion > currentVersion)
        {
            availableUpdate = new HubAvailableUpdate(
                version,
                platform,
                tag,
                new HubReleaseAsset(assetName, sha256, size));
        }
    }

    private bool IsAutomaticCheckFresh()
    {
        if (!hasChecked || checkedAtUtc is not { } checkedAt)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        return checkedAt <= now && now - checkedAt < AutomaticCheckInterval;
    }

    private void UpdateProgress(HubUpdateProgress value)
    {
        progress = value;
        NotifyStateChanged();
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HubLocalization.Text))
        {
            NotifyStateChanged();
        }
    }

    private string ProgressStatusText => progress?.Stage switch
    {
        HubUpdateStage.Downloading => Text.DownloadingSystemPackage(availableUpdate?.Version ?? string.Empty),
        HubUpdateStage.Verifying => Text.VerifyingSystemPackage,
        HubUpdateStage.LaunchingInstaller => Text.OpeningSystemPackageInstaller,
        HubUpdateStage.Installing => Text.InstallingSystemPackage,
        _ => string.Empty
    };

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CurrentVersionText));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(IsProjectActionVisible));
        OnPropertyChanged(nameof(ProjectActionText));
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(IsProgressVisible));
        OnPropertyChanged(nameof(IsProgressIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressText));
    }

    private static string FormatByteSize(long bytes)
    {
        const double megabyte = 1024d * 1024d;
        return bytes >= megabyte
            ? $"{bytes / megabyte:0.0} MB"
            : $"{bytes / 1024d:0.0} KB";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
