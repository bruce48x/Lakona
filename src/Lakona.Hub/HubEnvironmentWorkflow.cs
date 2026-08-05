using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lakona.Hub.Applications;
using Lakona.Hub.Sdk;

namespace Lakona.Hub;

internal sealed record HubEnvironmentStartupOutcome(
    string? ApplicationDetectionError,
    string? SdkInspectionError);

internal sealed record HubEnvironmentOperationOutcome(
    bool Succeeded,
    bool Changed,
    string? Error = null);

internal sealed record HubSdkInstallOutcome(
    bool Succeeded,
    string? Version = null,
    string? Error = null);

public sealed class HubEnvironmentWorkflow : INotifyPropertyChanged, IDisposable
{
    private readonly HubLocalization localization;
    private readonly IHubSdkManager sdkManager;
    private readonly HubApplicationRegistry applicationRegistry;
    private bool synchronizingApplications;
    private bool applicationDetectionComplete;
    private bool applicationDetectionFailed;
    private bool isDetectingApplications;
    private HubSdkStatus sdkStatus = new(false, HubSdkSource.None, null, null);
    private bool sdkInspectionComplete;
    private bool isInspectingSdk;
    private bool isInstallingSdk;
    private HubSdkProgress? sdkProgress;
    private string? sdkInstallError;

    internal HubEnvironmentWorkflow(
        HubLocalization localization,
        IHubSdkManager sdkManager,
        InstalledApplicationCatalog applicationCatalog,
        ManualApplicationStore manualApplicationStore,
        string? selectedServerEditorPath,
        IEnumerable<HubDetectedApplicationSettings> cachedApplications)
    {
        this.localization = localization;
        this.sdkManager = sdkManager;
        applicationRegistry = new HubApplicationRegistry(
            applicationCatalog,
            manualApplicationStore,
            localization,
            RestoreDetectedApplications(cachedApplications));
        ServerEditorSelection = new ServerEditorSelection(selectedServerEditorPath);
        ServerEditorSelection.Refresh(applicationRegistry.InstalledApplications);
        ServerEditorSelection.SelectionChanged += ServerEditorSelection_SelectionChanged;
        localization.PropertyChanged += Localization_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? ApplicationsChanged;

    public event EventHandler? PersistentStateChanged;

    public ObservableCollection<ApplicationToolItem> ApplicationTools => applicationRegistry.Tools;

    public ServerEditorSelection ServerEditorSelection { get; }

    internal IReadOnlyList<LocalApplicationInstallation> InstalledApplications =>
        applicationRegistry.InstalledApplications;

    internal string? SdkExecutablePath => sdkStatus.ExecutablePath;

    public string SdkStatusText => sdkInspectionComplete
        ? sdkStatus.IsReady ? Text.DotNetReady : Text.DotNetSdkMissing
        : Text.DetectingDotNetSdk;

    public string EnvironmentStatusText => sdkInspectionComplete
        ? sdkStatus.IsReady ? Text.EnvironmentReady : Text.EnvironmentNeedsSetup
        : Text.EnvironmentChecking;

    public string EnvironmentSummaryText => applicationDetectionFailed
        ? Text.ToolDetectionFailed
        : applicationDetectionComplete
            ? FormatEnvironmentSummary(applicationRegistry.InstalledApplications)
            : Text.DetectingTools;

    public string RuntimeTitle => !sdkInspectionComplete
        ? Text.DetectingDotNetSdk
        : sdkStatus.IsReady
            ? sdkStatus.Source == HubSdkSource.Managed
                ? Text.ManagedDotNetSdkReady
                : Text.SystemDotNetSdkReady
            : Text.DotNetSdkMissing;

    public string RuntimeVersion => $".NET SDK {sdkStatus.Version ?? HubRuntimeInfo.RequiredSdkVersion}";

    public string RuntimeDescription => !sdkInspectionComplete
        ? Text.DetectingDotNetSdkDescription
        : sdkStatus.IsReady
            ? sdkStatus.Source == HubSdkSource.Managed
                ? Text.ManagedDotNetSdkDescription
                : Text.SystemDotNetSdkDescription
            : Text.DotNetSdkMissingDescription;

    public bool IsDetectingApplications => isDetectingApplications;

    public bool CanRefreshApplications => !isDetectingApplications;

    public bool IsInspectingSdk => isInspectingSdk;

    public bool ShouldOfferSdkInstall => sdkInspectionComplete && !sdkStatus.IsReady;

    public bool IsInstallingSdk => isInstallingSdk;

    public bool CanInstallSdk => !isInstallingSdk;

    public bool CanDismissSdkInstall => !isInstallingSdk;

    public bool IsSdkInstallProgressVisible => sdkProgress is not null;

    public bool IsSdkInstallProgressIndeterminate => sdkProgress?.Stage != HubSdkInstallStage.Downloading;

    public double SdkInstallProgressValue => sdkProgress?.Percentage ?? 0;

    public string SdkInstallProgressText => sdkProgress switch
    {
        { Stage: HubSdkInstallStage.Downloading } progress => Text.DownloadProgress(
            progress.Percentage,
            FormatByteSize(progress.BytesReceived),
            progress.TotalBytes > 0 ? FormatByteSize(progress.TotalBytes) : Text.UnknownSize),
        { Stage: HubSdkInstallStage.Resolving } => Text.ResolvingDotNetSdkDownload,
        { Stage: HubSdkInstallStage.Verifying } => Text.VerifyingDotNetSdk,
        { Stage: HubSdkInstallStage.Extracting } => Text.ExtractingDotNetSdk,
        { Stage: HubSdkInstallStage.Validating } => Text.ValidatingDotNetSdk,
        { Stage: HubSdkInstallStage.Completed } => Text.DotNetSdkInstallComplete,
        _ => Text.ResolvingDotNetSdkDownload
    };

    public bool HasSdkInstallError => !string.IsNullOrWhiteSpace(sdkInstallError);

    public string SdkInstallErrorText => sdkInstallError is null
        ? string.Empty
        : Text.DotNetSdkInstallFailed(sdkInstallError);

    internal async Task<HubEnvironmentStartupOutcome> StartAsync(CancellationToken cancellationToken)
    {
        var applicationTask = DetectApplicationsAsync(cancellationToken);
        var sdkTask = InspectSdkAsync(cancellationToken);
        await Task.WhenAll(applicationTask, sdkTask);
        return new HubEnvironmentStartupOutcome(
            applicationTask.Result.Error,
            sdkTask.Result.Error);
    }

    internal async Task<HubEnvironmentOperationOutcome> DetectApplicationsAsync(
        CancellationToken cancellationToken)
    {
        if (isDetectingApplications)
        {
            return new HubEnvironmentOperationOutcome(false, false);
        }

        isDetectingApplications = true;
        applicationDetectionFailed = false;
        NotifyApplicationStateChanged();
        try
        {
            await applicationRegistry.DetectAsync(cancellationToken);
            applicationDetectionComplete = true;
            RefreshApplicationSelections();
            PersistentStateChanged?.Invoke(this, EventArgs.Empty);
            return new HubEnvironmentOperationOutcome(true, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            applicationDetectionFailed = true;
            return new HubEnvironmentOperationOutcome(false, false, exception.Message);
        }
        finally
        {
            isDetectingApplications = false;
            NotifyApplicationStateChanged();
        }
    }

    internal HubEnvironmentOperationOutcome AddManualApplication(
        ManualApplicationRegistration registration)
    {
        try
        {
            if (!applicationRegistry.TryAddManual(registration))
            {
                return new HubEnvironmentOperationOutcome(true, false);
            }

            RefreshApplicationSelections();
            PersistentStateChanged?.Invoke(this, EventArgs.Empty);
            return new HubEnvironmentOperationOutcome(true, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new HubEnvironmentOperationOutcome(false, false, exception.Message);
        }
    }

    internal HubEnvironmentOperationOutcome RemoveManualApplication(ApplicationToolItem tool)
    {
        try
        {
            if (!applicationRegistry.RemoveManual(tool))
            {
                return new HubEnvironmentOperationOutcome(true, false);
            }

            RefreshApplicationSelections();
            PersistentStateChanged?.Invoke(this, EventArgs.Empty);
            return new HubEnvironmentOperationOutcome(true, true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new HubEnvironmentOperationOutcome(false, false, exception.Message);
        }
    }

    internal void PrepareSdkInstall()
    {
        sdkProgress = null;
        sdkInstallError = null;
        NotifySdkInstallStateChanged();
    }

    internal async Task<HubSdkInstallOutcome> InstallSdkAsync(CancellationToken cancellationToken)
    {
        if (isInstallingSdk)
        {
            return new HubSdkInstallOutcome(false);
        }

        isInstallingSdk = true;
        sdkInstallError = null;
        sdkProgress = new HubSdkProgress(HubSdkInstallStage.Resolving);
        NotifySdkInstallStateChanged();
        try
        {
            var progress = new CallbackProgress<HubSdkProgress>(UpdateSdkProgress);
            sdkStatus = await sdkManager.InstallAsync(progress, cancellationToken);
            sdkInspectionComplete = true;
            NotifySdkStateChanged();
            return new HubSdkInstallOutcome(true, sdkStatus.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sdkProgress = null;
            throw;
        }
        catch (Exception exception)
        {
            sdkInstallError = exception.Message;
            return new HubSdkInstallOutcome(false, Error: exception.Message);
        }
        finally
        {
            isInstallingSdk = false;
            NotifySdkInstallStateChanged();
        }
    }

    internal HubDetectedApplicationSettings[] CaptureDetectedApplications() =>
        applicationRegistry.AutomaticApplications
            .Select(application => new HubDetectedApplicationSettings(
                application.Kind.ToString(),
                application.DisplayName,
                application.ExecutablePath,
                application.Version))
            .ToArray();

    public void Dispose()
    {
        localization.PropertyChanged -= Localization_PropertyChanged;
        ServerEditorSelection.SelectionChanged -= ServerEditorSelection_SelectionChanged;
        applicationRegistry.Dispose();
    }

    private HubText Text => localization.Text;

    private async Task<HubEnvironmentOperationOutcome> InspectSdkAsync(
        CancellationToken cancellationToken)
    {
        if (isInspectingSdk)
        {
            return new HubEnvironmentOperationOutcome(false, false);
        }

        isInspectingSdk = true;
        NotifySdkStateChanged();
        try
        {
            sdkStatus = await sdkManager.InspectAsync(cancellationToken);
            sdkInspectionComplete = true;
            NotifySdkStateChanged();
            return new HubEnvironmentOperationOutcome(true, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            sdkInspectionComplete = true;
            sdkStatus = new HubSdkStatus(false, HubSdkSource.None, null, null);
            NotifySdkStateChanged();
            return new HubEnvironmentOperationOutcome(false, false, exception.Message);
        }
        finally
        {
            isInspectingSdk = false;
            NotifySdkStateChanged();
        }
    }

    private void RefreshApplicationSelections()
    {
        synchronizingApplications = true;
        try
        {
            ServerEditorSelection.Refresh(applicationRegistry.InstalledApplications);
        }
        finally
        {
            synchronizingApplications = false;
        }

        ApplicationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ServerEditorSelection_SelectionChanged(object? sender, EventArgs e)
    {
        if (synchronizingApplications)
        {
            return;
        }

        ApplicationsChanged?.Invoke(this, EventArgs.Empty);
        PersistentStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSdkProgress(HubSdkProgress progress)
    {
        sdkProgress = progress;
        NotifySdkInstallStateChanged();
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HubLocalization.Text))
        {
            NotifyApplicationStateChanged();
            NotifySdkStateChanged();
            NotifySdkInstallStateChanged();
        }
    }

    private void NotifyApplicationStateChanged()
    {
        OnPropertyChanged(nameof(IsDetectingApplications));
        OnPropertyChanged(nameof(CanRefreshApplications));
        OnPropertyChanged(nameof(EnvironmentSummaryText));
    }

    private void NotifySdkStateChanged()
    {
        OnPropertyChanged(nameof(IsInspectingSdk));
        OnPropertyChanged(nameof(SdkStatusText));
        OnPropertyChanged(nameof(EnvironmentStatusText));
        OnPropertyChanged(nameof(RuntimeTitle));
        OnPropertyChanged(nameof(RuntimeVersion));
        OnPropertyChanged(nameof(RuntimeDescription));
        OnPropertyChanged(nameof(ShouldOfferSdkInstall));
    }

    private void NotifySdkInstallStateChanged()
    {
        OnPropertyChanged(nameof(IsInstallingSdk));
        OnPropertyChanged(nameof(CanInstallSdk));
        OnPropertyChanged(nameof(CanDismissSdkInstall));
        OnPropertyChanged(nameof(IsSdkInstallProgressVisible));
        OnPropertyChanged(nameof(IsSdkInstallProgressIndeterminate));
        OnPropertyChanged(nameof(SdkInstallProgressValue));
        OnPropertyChanged(nameof(SdkInstallProgressText));
        OnPropertyChanged(nameof(HasSdkInstallError));
        OnPropertyChanged(nameof(SdkInstallErrorText));
    }

    private string FormatEnvironmentSummary(IReadOnlyList<LocalApplicationInstallation> applications)
    {
        var names = applications
            .DistinctBy(application => application.Kind)
            .Select(application => application.DisplayName)
            .ToArray();
        return names.Length == 0
            ? Text.EnvironmentNone
            : Text.EnvironmentDetected(string.Join(Text.EnvironmentSeparator, names));
    }

    private static IReadOnlyList<LocalApplicationInstallation> RestoreDetectedApplications(
        IEnumerable<HubDetectedApplicationSettings> applications) =>
        applications
            .Where(application => Enum.TryParse<LocalApplicationKind>(application.Kind, out var kind) && Enum.IsDefined(kind))
            .Select(application => new LocalApplicationInstallation(
                Enum.Parse<LocalApplicationKind>(application.Kind),
                application.DisplayName,
                application.ExecutablePath,
                application.Version))
            .ToArray();

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
