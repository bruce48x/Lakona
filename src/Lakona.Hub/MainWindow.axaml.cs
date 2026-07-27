using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lakona.Hub.Applications;
using Lakona.Hub.Sdk;
using Lakona.Hub.Updates;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed partial class MainWindow : Window
{
    private const string HelpIssuesUrl = "https://github.com/bruce48x/Lakona/issues";
    private StoredHubCrashReport? pendingCrashReport;

    private readonly LakonaProjectInspector inspector = new();
    private readonly LakonaProjectCreator projectCreator = new();
    private readonly HubApplicationRegistry applicationRegistry;
    private readonly HubUserSettingsPersistence userSettingsPersistence;
    private readonly HubWindowLifetime windowLifetime = new();
    private readonly HubNavigationState navigationState;
    private readonly ApplicationLauncher applicationLauncher = new();
    private readonly HubProjectBrowser projectBrowser = new();
    private readonly IHubUpdateService updateService;
    private readonly IHubSdkManager sdkManager;
    private bool environmentDetectionComplete;
    private bool environmentDetectionFailed;
    private bool isUpdating;
    private bool hasCheckedForUpdates;
    private DateTimeOffset? lastUpdateCheckedAtUtc;
    private HubAvailableUpdate? availableUpdate;
    private HubWindowSettings? restoredWindowSettings;
    private HubSdkStatus sdkStatus = new(false, HubSdkSource.None, null, null);
    private bool sdkInspectionComplete;
    private bool isInstallingSdk;
    private bool synchronizingProjectSearch;
    private CancellationTokenSource? feedbackCancellation;
    private CancellationTokenSource? experienceCancellation;
    private Control? activeExperience;
    private readonly DispatcherTimer lastOpenedRefreshTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    public MainWindow()
        : this(LoadStartupSettings(), enableStartupDetection: true)
    {
    }

    internal MainWindow(bool enableStartupDetection)
        : this(LoadStartupSettings(), enableStartupDetection)
    {
    }

    private MainWindow(HubStartupSettings startupSettings, bool enableStartupDetection)
        : this(
            new HubLocalization(startupSettings.Settings.Language),
            new HubUpdateService(),
            new HubSdkManager(),
            new InstalledApplicationCatalog(),
            new ManualApplicationStore(),
            startupSettings.Store,
            startupSettings.Settings,
            enableStartupDetection)
    {
    }

    internal MainWindow(HubLocalization localization)
        : this(localization, new HubUpdateService(), new InstalledApplicationCatalog(), new ManualApplicationStore())
    {
    }

    internal MainWindow(HubLocalization localization, IHubUpdateService updateService)
        : this(localization, updateService, new InstalledApplicationCatalog(), new ManualApplicationStore())
    {
    }

    internal MainWindow(
        HubLocalization localization,
        IHubUpdateService updateService,
        InstalledApplicationCatalog applicationCatalog,
        ManualApplicationStore manualApplicationStore,
        bool enableStartupDetection = true)
        : this(
            localization,
            updateService,
            new HubSdkManager(),
            applicationCatalog,
            manualApplicationStore,
            new HubUserSettingsStore(),
            new HubUserSettings(localization.Language, [], [], null, "Projects", null, null),
            enableStartupDetection)
    {
    }

    private MainWindow(
        HubLocalization localization,
        IHubUpdateService updateService,
        IHubSdkManager sdkManager,
        InstalledApplicationCatalog applicationCatalog,
        ManualApplicationStore manualApplicationStore,
        HubUserSettingsStore userSettingsStore,
        HubUserSettings settings,
        bool enableStartupDetection)
    {
        Localization = localization;
        this.updateService = updateService;
        this.sdkManager = sdkManager;
        applicationRegistry = new HubApplicationRegistry(
            applicationCatalog,
            manualApplicationStore,
            localization,
            RestoreDetectedApplications(settings.DetectedApplications));
        navigationState = new HubNavigationState(
            settings.CurrentPage == "Settings" ? HubPage.Settings : HubPage.Projects);
        restoredWindowSettings = settings.Window;
        RestoreUpdateCheck(settings.UpdateCheck);
        CreationForm = new ProjectCreationForm(localization);
        CreationForm.ApplyDraft(settings.CreationDraft);
        InitializeComponent();
        RestoreWindow(settings.Window);
        DataContext = this;
        userSettingsPersistence = new HubUserSettingsPersistence(CaptureUserSettings, userSettingsStore.Save);
        userSettingsPersistence.SaveFailed += UserSettingsPersistence_SaveFailed;
        lastOpenedRefreshTimer.Tick += (_, _) =>
        {
            projectBrowser.RefreshLastOpened();
        };
        if (enableStartupDetection)
        {
            Opened += MainWindow_Opened;
        }
        PropertyChanged += MainWindow_PropertyChanged;
        Localization.PropertyChanged += Localization_PropertyChanged;
        CreationForm.PropertyChanged += CreationForm_PropertyChanged;
        projectBrowser.ViewChanged += ProjectBrowser_ViewChanged;
        projectBrowser.PersistentStateChanged += ProjectBrowser_PersistentStateChanged;
        Closing += MainWindow_Closing;
        RestoreProjects(settings.Projects);
        UpdateWindowFrame();
        UpdateEnvironmentTexts();
        UpdateUpdateTexts();
        UpdateSdkTexts();
        UpdateExperience();
    }

    public ObservableCollection<ProjectListItem> Projects => projectBrowser.Projects;

    public ObservableCollection<ProjectListItem> VisibleProjects => projectBrowser.VisibleProjects;

    public ObservableCollection<ApplicationToolItem> ApplicationTools => applicationRegistry.Tools;

    public ProjectCreationForm CreationForm { get; }

    public HubLocalization Localization { get; }

    public HubStatusSummary StatusSummary { get; } = new();

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        lastOpenedRefreshTimer.Start();
        try
        {
            await Task.WhenAll(
                DetectApplicationsAsync(showFailureFeedback: true, windowLifetime.Token),
                RefreshSdkStatusAsync(showInstallPrompt: true, windowLifetime.Token));
        }
        catch (OperationCanceledException) when (windowLifetime.IsClosing)
        {
        }
    }

    private async void ImportProject_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localization.Text.SelectProjectFolder,
            AllowMultiple = false
        });
        var folder = folders.FirstOrDefault();
        if (folder?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        ShowInspection(inspector.Inspect(path));
    }

    private void ShowInspection(LakonaProjectInspection inspection)
    {
        string? settingsSaveError = null;
        if (inspection.Status is LakonaProjectStatus.Ready or LakonaProjectStatus.Incomplete)
        {
            var item = ProjectListItem.FromInspection(
                inspection,
                applicationRegistry.InstalledApplications,
                Localization);
            projectBrowser.AddOrReplace(item);
            settingsSaveError = TrySaveUserSettings();
            UpdateExperience();
        }

        ShowFeedback(settingsSaveError ?? (inspection.Status switch
        {
            LakonaProjectStatus.Ready => Localization.Text.Imported(inspection.Name),
            LakonaProjectStatus.Incomplete => Localization.Text.ImportedIncomplete(inspection.Name, inspection.Diagnostics.Count),
            LakonaProjectStatus.NotLakonaProject => Localization.Text.NotLakonaProject,
            LakonaProjectStatus.NotFound => Localization.Text.ProjectNotFound,
            _ => Localization.Text.ProjectUnrecognized
        }));
    }

    private void ProjectList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ProjectListItem project })
        {
            ShowFeedback(Localization.Text.ProjectSelection(project.Name, project.StatusText, project.Path));
        }
    }

    private void ProjectExperience_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var useWideLayout = HubProjectLayout.UseWideLayout(e.NewSize.Width);
        WideProjectToolbar.IsVisible = useWideLayout;
        WideProjectTable.IsVisible = useWideLayout;
        WideProjectList.IsVisible = useWideLayout;
        CompactProjectToolbar.IsVisible = !useWideLayout;
        CompactProjectList.IsVisible = !useWideLayout;
    }

    private void OpenServer_Click(object? sender, RoutedEventArgs e)
    {
        var project = ProjectFromSender(sender);
        if (project?.SelectedServerEditor is not { } editor)
        {
            ShowFeedback(Localization.Text.NoSupportedIde);
            return;
        }

        try
        {
            applicationLauncher.Launch(ApplicationLaunchPlanner.OpenServerDirectory(project.ServerPath, editor));
            project.MarkOpened();
            ShowFeedback(Localization.Text.OpeningServer(editor.DisplayName, project.Name));
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            ShowFeedback(Localization.Text.OpenServerFailed(ex.Message));
        }
    }

    private void OpenClient_Click(object? sender, RoutedEventArgs e)
    {
        var project = ProjectFromSender(sender);
        if (project?.ClientApplication is not { } application)
        {
            ShowFeedback(Localization.Text.NoMatchingClientEditor);
            return;
        }

        try
        {
            applicationLauncher.Launch(ApplicationLaunchPlanner.OpenClientDirectory(
                project.ClientPath,
                project.ClientKind,
                application));
            project.MarkOpened();
            ShowFeedback(Localization.Text.OpeningClient(application.DisplayName, project.Name));
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            ShowFeedback(Localization.Text.OpenClientFailed(ex.Message));
        }
    }

    private void OpenProjectFolder_Click(object? sender, RoutedEventArgs e)
    {
        var project = ProjectFromSender(sender);
        if (project is null)
        {
            return;
        }

        if (!Directory.Exists(project.Path))
        {
            ShowFeedback(Localization.Text.ProjectNotFound);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(project.Path)
            {
                UseShellExecute = true
            };
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("The project folder could not be opened.");
            }

            project.MarkOpened();
            ShowFeedback(Localization.Text.OpeningProjectFolder(project.Name));
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            ShowFeedback(Localization.Text.OpenProjectFolderFailed(ex.Message));
        }
    }

    private void ProjectMore_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.Open(button);
        }
    }

    private void RemoveProjectFromList_Click(object? sender, RoutedEventArgs e)
    {
        var project = ProjectFromSender(sender);
        if (project is null || !projectBrowser.Remove(project))
        {
            return;
        }

        var settingsSaveError = TrySaveUserSettings();
        UpdateExperience();
        ShowFeedback(settingsSaveError ?? Localization.Text.ProjectRemoved(project.Name));
    }

    private void CreateProject_Click(object? sender, RoutedEventArgs e)
    {
        navigationState.StartCreating();
        HideFeedback();
        UpdateExperience();
    }

    private async void BrowseProjectOutput_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localization.Text.SelectOutputFolder,
            AllowMultiple = false
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            CreationForm.OutputDirectory = path;
        }
    }

    private void CancelCreateProject_Click(object? sender, RoutedEventArgs e)
    {
        navigationState.CancelCreating();
        HideFeedback();
        UpdateExperience();
    }

    private async void ContinueCreateProject_Click(object? sender, RoutedEventArgs e)
    {
        if (CreationForm.IsCreating)
        {
            return;
        }

        if (!CreationForm.CanCreate)
        {
            ShowFeedback(CreationForm.ValidationMessage);
            return;
        }

        CreationForm.IsCreating = true;
        ShowFeedback(Localization.Text.CreatingProject(CreationForm.ProjectName.Trim()));
        try
        {
            var result = await projectCreator.CreateAsync(CreationForm.CreateRequest(), windowLifetime.Token);
            navigationState.CancelCreating();
            ShowInspection(inspector.Inspect(result.RootPath));
            ShowFeedback(Localization.Text.ProjectCreated(CreationForm.ProjectName.Trim()));
        }
        catch (Exception ex) when (ex is LakonaProjectCreationException or IOException or UnauthorizedAccessException)
        {
            ShowFeedback(Localization.Text.ProjectCreationFailed(ex.Message));
        }
        catch (OperationCanceledException) when (windowLifetime.IsClosing)
        {
        }
        finally
        {
            CreationForm.IsCreating = false;
        }
    }

    private void Projects_Click(object? sender, RoutedEventArgs e)
    {
        navigationState.Navigate(HubPage.Projects);
        HideFeedback();
        UpdateExperience();
        ScheduleUserSettingsSave();
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        navigationState.Navigate(HubPage.Settings);
        HideFeedback();
        UpdateExperience();
        ScheduleUserSettingsSave();
    }

    private async void RefreshEnvironment_Click(object? sender, RoutedEventArgs e)
    {
        await DetectApplicationsAsync(showFailureFeedback: true, windowLifetime.Token);
    }

    private async void ApplicationToolAction_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ApplicationToolItem tool })
        {
            return;
        }

        if (tool.IsManual)
        {
            RemoveManualApplication(tool);
            return;
        }

        var path = await PickApplicationExecutableAsync(
            Localization.Text.SelectApplicationExecutable(tool.DisplayName),
            tool.SuggestedPath);
        if (path is null)
        {
            return;
        }

        if (!SystemApplicationProbeSource.TryCreateInstallation(tool.Kind, path, out var installation))
        {
            ShowFeedback(Localization.Text.InvalidApplicationExecutable(tool.DisplayName));
            return;
        }

        AddManualApplication(new ManualApplicationRegistration(
            installation.Kind,
            installation.DisplayName,
            installation.ExecutablePath));
    }

    private async void AddApplicationTool_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickApplicationExecutableAsync(Localization.Text.SelectToolExecutable, null);
        if (path is null)
        {
            return;
        }

        if (!SystemApplicationProbeSource.TryCreateManualInstallation(path, out var installation))
        {
            ShowFeedback(Localization.Text.InvalidApplicationExecutable(Localization.Text.DetectedTools));
            return;
        }

        AddManualApplication(new ManualApplicationRegistration(
            installation.Kind,
            installation.DisplayName,
            installation.ExecutablePath));
    }

    private async Task<string?> PickApplicationExecutableAsync(string title, string? suggestedPath)
    {
        var startDirectory = ResolveApplicationPickerDirectory(suggestedPath);
        IStorageFolder? suggestedStartLocation = null;
        try
        {
            suggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                new Uri(Path.GetFullPath(startDirectory)));
        }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or IOException)
        {
            // The picker remains usable even when the preferred starting directory is unavailable.
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = suggestedStartLocation
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void AddManualApplication(ManualApplicationRegistration registration)
    {
        try
        {
            if (!applicationRegistry.TryAddManual(registration))
            {
                ShowFeedback(Localization.Text.ApplicationAlreadyAdded(registration.DisplayName));
                return;
            }

            projectBrowser.RefreshApplications(applicationRegistry.InstalledApplications);
            UpdateEnvironmentTexts();
            ShowFeedback(Localization.Text.ApplicationExecutableSaved(registration.DisplayName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowFeedback(Localization.Text.ApplicationExecutableSaveFailed(ex.Message));
        }
    }

    private void RemoveManualApplication(ApplicationToolItem tool)
    {
        try
        {
            if (!applicationRegistry.RemoveManual(tool))
            {
                return;
            }

            projectBrowser.RefreshApplications(applicationRegistry.InstalledApplications);
            UpdateEnvironmentTexts();
            ShowFeedback(Localization.Text.ApplicationRemoved(tool.DisplayName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowFeedback(Localization.Text.ApplicationExecutableSaveFailed(ex.Message));
        }
    }

    private async void CheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        if (isUpdating)
        {
            return;
        }

        isUpdating = true;
        UpdateButton.IsEnabled = false;
        try
        {
            if (availableUpdate is null)
            {
                hasCheckedForUpdates = false;
                UpdateStatusText.Text = Localization.Text.CheckingForUpdates;
                availableUpdate = await updateService.CheckAsync(windowLifetime.Token);
                hasCheckedForUpdates = true;
                lastUpdateCheckedAtUtc = DateTimeOffset.UtcNow;
                ScheduleUserSettingsSave();
                UpdateUpdateTexts();
                return;
            }

            UpdateDownloadProgressPanel.IsVisible = true;
            UpdateDownloadProgress.IsIndeterminate = false;
            UpdateDownloadProgress.Value = 0;
            UpdateDownloadProgressText.Text = "0%";
            var progress = new InlineProgress<HubUpdateProgress>(UpdateDownloadProgressState);
            await updateService.PrepareAndLaunchAsync(availableUpdate, progress, windowLifetime.Token);
            UpdateDownloadProgressPanel.IsVisible = false;
            UpdateStatusText.Text = availableUpdate.Platform.StartsWith("linux-", StringComparison.Ordinal)
                ? Localization.Text.SystemPackageInstalled
                : Localization.Text.SystemPackageInstallerOpened;
        }
        catch (OperationCanceledException) when (windowLifetime.IsClosing)
        {
        }
        catch (Exception ex)
        {
            UpdateDownloadProgressPanel.IsVisible = false;
            UpdateStatusText.Text = Localization.Text.UpdateFailed(ex.Message);
        }
        finally
        {
            isUpdating = false;
            if (!windowLifetime.IsClosing)
            {
                UpdateButton.IsEnabled = true;
            }
        }
    }

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        HideFeedback();
        HelpDialogOverlay.IsVisible = true;
    }

    internal void ShowPendingCrashReport()
    {
        pendingCrashReport = HubCrashReporter.PendingReport;
        if (pendingCrashReport is null)
        {
            return;
        }

        CrashReportSummaryText.Text = Localization.Text.PreviousCrashSummary(
            pendingCrashReport.OccurredAtUtc.ToLocalTime(),
            pendingCrashReport.Activity);
        CrashReportOverlay.IsVisible = true;
    }

    private void DismissCrashReport_Click(object? sender, RoutedEventArgs e)
    {
        CrashReportOverlay.IsVisible = false;
        pendingCrashReport = null;
        HubCrashReporter.Acknowledge();
    }

    private void SendCrashReport_Click(object? sender, RoutedEventArgs e)
    {
        if (pendingCrashReport is null)
        {
            CrashReportOverlay.IsVisible = false;
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo(HubCrashReporter.CreateIssueUrl(pendingCrashReport))
            {
                UseShellExecute = true
            };
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("The default browser could not be started.");
            }

            CrashReportOverlay.IsVisible = false;
            pendingCrashReport = null;
            HubCrashReporter.Acknowledge();
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            ShowFeedback(Localization.Text.OpenHelpPageFailed(ex.Message));
        }
    }

    private async Task RefreshSdkStatusAsync(bool showInstallPrompt, CancellationToken cancellationToken)
    {
        sdkInspectionComplete = false;
        RuntimeDetectionProgress.IsVisible = true;
        UpdateSdkTexts();
        try
        {
            sdkStatus = await sdkManager.InspectAsync(cancellationToken);
            sdkInspectionComplete = true;
            UpdateSdkTexts();
            if (showInstallPrompt && !sdkStatus.IsReady)
            {
                ShowSdkInstallPrompt();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            sdkInspectionComplete = true;
            sdkStatus = new HubSdkStatus(false, HubSdkSource.None, null, null);
            UpdateSdkTexts();
            ShowFeedback(Localization.Text.DotNetSdkDetectionFailed(ex.Message));
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                RuntimeDetectionProgress.IsVisible = false;
            }
        }
    }

    private void UpdateSdkTexts()
    {
        StatusSummary.SdkStatusText = sdkInspectionComplete
            ? sdkStatus.IsReady ? Localization.Text.DotNetReady : Localization.Text.DotNetSdkMissing
            : Localization.Text.DetectingDotNetSdk;
        EnvironmentStatusText.Text = sdkInspectionComplete
            ? sdkStatus.IsReady ? Localization.Text.EnvironmentReady : Localization.Text.EnvironmentNeedsSetup
            : Localization.Text.EnvironmentChecking;

        if (!sdkInspectionComplete)
        {
            RuntimeTitleText.Text = Localization.Text.DetectingDotNetSdk;
            RuntimeVersionText.Text = $".NET SDK {HubRuntimeInfo.RequiredSdkVersion}";
            RuntimeDescriptionText.Text = Localization.Text.DetectingDotNetSdkDescription;
            InstallSdkButton.IsVisible = false;
            return;
        }

        if (sdkStatus.IsReady)
        {
            RuntimeTitleText.Text = sdkStatus.Source == HubSdkSource.Managed
                ? Localization.Text.ManagedDotNetSdkReady
                : Localization.Text.SystemDotNetSdkReady;
            RuntimeVersionText.Text = $".NET SDK {sdkStatus.Version}";
            RuntimeDescriptionText.Text = sdkStatus.Source == HubSdkSource.Managed
                ? Localization.Text.ManagedDotNetSdkDescription
                : Localization.Text.SystemDotNetSdkDescription;
            InstallSdkButton.IsVisible = false;
            return;
        }

        RuntimeTitleText.Text = Localization.Text.DotNetSdkMissing;
        RuntimeVersionText.Text = $".NET SDK {HubRuntimeInfo.RequiredSdkVersion}";
        RuntimeDescriptionText.Text = Localization.Text.DotNetSdkMissingDescription;
        InstallSdkButton.IsVisible = true;
    }

    private void ShowSdkInstallPrompt_Click(object? sender, RoutedEventArgs e) => ShowSdkInstallPrompt();

    private void ShowSdkInstallPrompt()
    {
        SdkInstallErrorText.IsVisible = false;
        SdkInstallProgressPanel.IsVisible = false;
        SdkInstallOverlay.IsVisible = true;
    }

    private void CancelSdkInstall_Click(object? sender, RoutedEventArgs e)
    {
        if (!isInstallingSdk)
        {
            SdkInstallOverlay.IsVisible = false;
        }
    }

    private async void InstallSdk_Click(object? sender, RoutedEventArgs e)
    {
        if (isInstallingSdk)
        {
            return;
        }

        isInstallingSdk = true;
        ConfirmSdkInstallButton.IsEnabled = false;
        CancelSdkInstallButton.IsEnabled = false;
        SdkInstallErrorText.IsVisible = false;
        SdkInstallProgressPanel.IsVisible = true;
        SdkInstallProgress.IsIndeterminate = true;
        SdkInstallProgressText.Text = Localization.Text.ResolvingDotNetSdkDownload;
        try
        {
            var progress = new InlineProgress<HubSdkProgress>(UpdateSdkInstallProgress);
            sdkStatus = await sdkManager.InstallAsync(progress, windowLifetime.Token);
            sdkInspectionComplete = true;
            UpdateSdkTexts();
            UpdateWindowFrame();
            RefreshProjectView();
            SdkInstallOverlay.IsVisible = false;
            ShowFeedback(Localization.Text.DotNetSdkInstalled(sdkStatus.Version ?? HubRuntimeInfo.RequiredSdkVersion));
        }
        catch (OperationCanceledException) when (windowLifetime.IsClosing)
        {
        }
        catch (Exception ex)
        {
            SdkInstallErrorText.Text = Localization.Text.DotNetSdkInstallFailed(ex.Message);
            SdkInstallErrorText.IsVisible = true;
        }
        finally
        {
            isInstallingSdk = false;
            if (!windowLifetime.IsClosing)
            {
                ConfirmSdkInstallButton.IsEnabled = true;
                CancelSdkInstallButton.IsEnabled = true;
            }
        }
    }

    private void UpdateSdkInstallProgress(HubSdkProgress progress)
    {
        SdkInstallProgressPanel.IsVisible = true;
        SdkInstallProgress.IsIndeterminate = progress.Stage != HubSdkInstallStage.Downloading;
        if (progress.Stage == HubSdkInstallStage.Downloading)
        {
            SdkInstallProgress.Value = progress.Percentage;
            SdkInstallProgressText.Text = Localization.Text.DownloadProgress(
                progress.Percentage,
                FormatByteSize(progress.BytesReceived),
                progress.TotalBytes > 0 ? FormatByteSize(progress.TotalBytes) : Localization.Text.UnknownSize);
            return;
        }

        SdkInstallProgressText.Text = progress.Stage switch
        {
            HubSdkInstallStage.Resolving => Localization.Text.ResolvingDotNetSdkDownload,
            HubSdkInstallStage.Verifying => Localization.Text.VerifyingDotNetSdk,
            HubSdkInstallStage.Extracting => Localization.Text.ExtractingDotNetSdk,
            HubSdkInstallStage.Validating => Localization.Text.ValidatingDotNetSdk,
            HubSdkInstallStage.Completed => Localization.Text.DotNetSdkInstallComplete,
            _ => Localization.Text.ResolvingDotNetSdkDownload
        };
    }

    private void CancelHelp_Click(object? sender, RoutedEventArgs e)
    {
        HelpDialogOverlay.IsVisible = false;
    }

    private void OpenHelpIssues_Click(object? sender, RoutedEventArgs e)
    {
        HelpDialogOverlay.IsVisible = false;
        try
        {
            var startInfo = new ProcessStartInfo(HelpIssuesUrl)
            {
                UseShellExecute = true
            };
            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("The default browser could not be started.");
            }
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            ShowFeedback(Localization.Text.OpenHelpPageFailed(ex.Message));
        }
    }

    private void UpdateExperience()
    {
        var onProjectsPage = navigationState.CurrentPage == HubPage.Projects;
        Control next = navigationState.Experience(Projects.Count > 0) switch
        {
            HubExperience.Settings => SettingsExperience,
            HubExperience.CreateProject => CreateExperience,
            HubExperience.Projects => ProjectExperience,
            _ => EmptyExperience
        };
        SwitchExperience(next);
        ProjectsNavButton.Classes.Set("selected", onProjectsPage);
        SettingsNavButton.Classes.Set("selected", navigationState.CurrentPage == HubPage.Settings);
    }

    private async Task DetectApplicationsAsync(bool showFailureFeedback, CancellationToken cancellationToken)
    {
        environmentDetectionComplete = false;
        environmentDetectionFailed = false;
        EnvironmentDetectionProgress.IsVisible = true;
        RefreshEnvironmentButton.IsEnabled = false;
        UpdateEnvironmentTexts();
        try
        {
            await applicationRegistry.DetectAsync(cancellationToken);
            projectBrowser.RefreshApplications(applicationRegistry.InstalledApplications);
            environmentDetectionComplete = true;
            ScheduleUserSettingsSave();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            environmentDetectionFailed = true;
            if (showFailureFeedback)
            {
                ShowFeedback(Localization.Text.ToolDetectionError(ex.Message));
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                EnvironmentDetectionProgress.IsVisible = false;
                RefreshEnvironmentButton.IsEnabled = true;
                UpdateEnvironmentTexts();
            }
        }
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HubLocalization.Text))
        {
            HideFeedback();
            UpdateEnvironmentTexts();
            UpdateUpdateTexts();
            UpdateSdkTexts();
            var settingsSaveError = TrySaveUserSettings();
            if (settingsSaveError is not null)
            {
                ShowFeedback(settingsSaveError);
            }
        }
    }

    private void UpdateUpdateTexts()
    {
        CurrentHubVersionText.Text = Localization.Text.CurrentHubVersion(updateService.CurrentVersion);
        if (availableUpdate is null)
        {
            UpdateStatusText.Text = hasCheckedForUpdates
                ? Localization.Text.NoUpdatesAvailable(updateService.CurrentVersion)
                : Localization.Text.UpdateCheckDescription;
            UpdateButtonText.Text = Localization.Text.CheckForUpdates;
        }
        else
        {
            UpdateStatusText.Text = Localization.Text.SystemPackageUpdateAvailable(availableUpdate.Version);
            UpdateButtonText.Text = Localization.Text.DownloadAndInstall;
        }
    }

    private void UpdateDownloadProgressState(HubUpdateProgress progress)
    {
        switch (progress.Stage)
        {
            case HubUpdateStage.Downloading:
                UpdateDownloadProgressPanel.IsVisible = true;
                UpdateDownloadProgress.IsIndeterminate = false;
                UpdateDownloadProgress.Value = progress.Percentage;
                UpdateDownloadProgressText.Text = Localization.Text.DownloadProgress(
                    progress.Percentage,
                    FormatByteSize(progress.BytesReceived),
                    FormatByteSize(progress.TotalBytes));
                UpdateStatusText.Text = Localization.Text.DownloadingSystemPackage(availableUpdate?.Version ?? string.Empty);
                break;
            case HubUpdateStage.Verifying:
                UpdateDownloadProgress.IsIndeterminate = true;
                UpdateDownloadProgressText.Text = Localization.Text.VerifyingSystemPackage;
                UpdateStatusText.Text = Localization.Text.VerifyingSystemPackage;
                break;
            case HubUpdateStage.LaunchingInstaller:
                UpdateDownloadProgress.IsIndeterminate = true;
                UpdateDownloadProgressText.Text = Localization.Text.OpeningSystemPackageInstaller;
                break;
            case HubUpdateStage.Installing:
                UpdateDownloadProgress.IsIndeterminate = true;
                UpdateDownloadProgressText.Text = Localization.Text.InstallingSystemPackage;
                UpdateStatusText.Text = Localization.Text.InstallingSystemPackage;
                break;
        }
    }

    private static ProjectListItem? ProjectFromSender(object? sender) =>
        sender is Control { DataContext: ProjectListItem project } ? project : null;

    private static string FormatByteSize(long bytes)
    {
        const double megabyte = 1024d * 1024d;
        return bytes >= megabyte
            ? $"{bytes / megabyte:0.0} MB"
            : $"{bytes / 1024d:0.0} KB";
    }

    private void UpdateEnvironmentTexts()
    {
        var summary = environmentDetectionFailed
            ? Localization.Text.ToolDetectionFailed
            : environmentDetectionComplete
                ? FormatEnvironmentSummary(applicationRegistry.InstalledApplications)
                : Localization.Text.DetectingTools;
        StatusSummary.EnvironmentSummaryText = summary;
    }

    private static string ResolveApplicationPickerDirectory(string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var directory = Path.GetDirectoryName(executablePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private void RestoreProjects(IEnumerable<HubProjectSettings> projects)
    {
        foreach (var project in projects)
        {
            var inspection = inspector.Inspect(project.Path);
            var item = ProjectListItem.FromInspection(
                inspection,
                applicationRegistry.InstalledApplications,
                Localization,
                project.SelectedServerEditorPath,
                project.LastOpenedAtUtc);
            projectBrowser.AddRestored(item);
        }
        RefreshProjectView();
    }

    private HubUserSettings CaptureUserSettings() => new(
        Localization.Language,
        Projects.Select(project => new HubProjectSettings(
            project.Path,
            project.SelectedServerEditor?.ExecutablePath,
            project.LastOpenedAtUtc)).ToArray(),
        applicationRegistry.AutomaticApplications.Select(application => new HubDetectedApplicationSettings(
            application.Kind.ToString(),
            application.DisplayName,
            application.ExecutablePath,
            application.Version)).ToArray(),
        CreationForm.CaptureDraft(),
        navigationState.CurrentPage.ToString(),
        CaptureWindowSettings(),
        CaptureUpdateCheck());

    private string? TrySaveUserSettings()
    {
        var error = userSettingsPersistence.SaveNow();
        return error is null ? null : Localization.Text.UserSettingsSaveFailed(error.Message);
    }

    private void ScheduleUserSettingsSave() => _ = userSettingsPersistence.ScheduleSave();

    private void UserSettingsPersistence_SaveFailed(Exception exception)
    {
        if (windowLifetime.IsClosing)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!windowLifetime.IsClosing)
            {
                ShowFeedback(Localization.Text.UserSettingsSaveFailed(exception.Message));
            }
        });
    }

    private static HubStartupSettings LoadStartupSettings()
    {
        var store = new HubUserSettingsStore();
        var detectedLanguage = HubLocalization.DetectLanguage(CultureInfo.CurrentUICulture);
        return new HubStartupSettings(store, store.Load(detectedLanguage));
    }

    private sealed record HubStartupSettings(HubUserSettingsStore Store, HubUserSettings Settings);

    private void ProjectBrowser_ViewChanged(object? sender, EventArgs e)
    {
        NoMatchingProjectsText.IsVisible = projectBrowser.HasNoMatches;
        UpdateSortButtonLabels();
    }

    private void ProjectBrowser_PersistentStateChanged(object? sender, EventArgs e) =>
        ScheduleUserSettingsSave();

    private void CreationForm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectCreationForm.ProjectName) or
            nameof(ProjectCreationForm.OutputDirectory) or
            nameof(ProjectCreationForm.SelectedClient) or
            nameof(ProjectCreationForm.SelectedClientVersion) or
            nameof(ProjectCreationForm.SelectedTransport) or
            nameof(ProjectCreationForm.SelectedSerializer) or
            nameof(ProjectCreationForm.SelectedNuGetForUnitySource))
        {
            ScheduleUserSettingsSave();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        windowLifetime.Close();
        lastOpenedRefreshTimer.Stop();
        feedbackCancellation?.Cancel();
        experienceCancellation?.Cancel();
        CreationForm.PropertyChanged -= CreationForm_PropertyChanged;
        Localization.PropertyChanged -= Localization_PropertyChanged;
        PropertyChanged -= MainWindow_PropertyChanged;
        TrySaveUserSettings();
        userSettingsPersistence.SaveFailed -= UserSettingsPersistence_SaveFailed;
        userSettingsPersistence.Dispose();
        projectBrowser.ViewChanged -= ProjectBrowser_ViewChanged;
        projectBrowser.PersistentStateChanged -= ProjectBrowser_PersistentStateChanged;
        projectBrowser.Dispose();
        applicationRegistry.Dispose();
        windowLifetime.Dispose();
    }

    private HubWindowSettings CaptureWindowSettings()
    {
        var width = Math.Max(MinWidth, Bounds.Width);
        var height = Math.Max(MinHeight, Bounds.Height);
        if (WindowState == WindowState.Maximized && restoredWindowSettings is not null)
        {
            width = restoredWindowSettings.Width;
            height = restoredWindowSettings.Height;
        }

        return new HubWindowSettings(
            Position.X,
            Position.Y,
            width,
            height,
            WindowState == WindowState.Maximized ? "Maximized" : "Normal");
    }

    private void RestoreWindow(HubWindowSettings? window)
    {
        if (window is null)
        {
            return;
        }

        Width = window.Width;
        Height = window.Height;
        Position = new PixelPoint(window.X, window.Y);
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = window.State == "Maximized" ? WindowState.Maximized : WindowState.Normal;
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

    private void RestoreUpdateCheck(HubUpdateCheckSettings? update)
    {
        if (update is null)
        {
            return;
        }

        hasCheckedForUpdates = true;
        lastUpdateCheckedAtUtc = update.CheckedAtUtc;
        if (update is { Version: { } version, Platform: { } platform, Tag: { } tag,
                       AssetName: { } assetName, Sha256: { } sha256, Size: { } size } &&
            Version.TryParse(version, out var availableVersion) &&
            Version.TryParse(updateService.CurrentVersion, out var currentVersion) &&
            availableVersion > currentVersion)
        {
            availableUpdate = new HubAvailableUpdate(version, platform, tag, new HubReleaseAsset(assetName, sha256, size));
        }
    }

    private HubUpdateCheckSettings? CaptureUpdateCheck() => !hasCheckedForUpdates
        ? null
        : availableUpdate is null
            ? new HubUpdateCheckSettings(lastUpdateCheckedAtUtc ?? DateTimeOffset.UtcNow, null, null, null, null, null, null)
            : new HubUpdateCheckSettings(
                lastUpdateCheckedAtUtc ?? DateTimeOffset.UtcNow,
                availableUpdate.Version,
                availableUpdate.Platform,
                availableUpdate.Tag,
                availableUpdate.Asset.AssetName,
                availableUpdate.Asset.Sha256,
                availableUpdate.Asset.Size);

    private void MainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            UpdateWindowFrame();
        }
    }

    private void UpdateWindowFrame()
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowFrame.Classes.Set("maximized", maximized);
        MaximizeGlyph.IsVisible = !maximized;
        RestoreGlyph.IsVisible = maximized;
        ToolTip.SetTip(MaximizeButton, maximized ? Localization.Text.Restore : Localization.Text.Maximize);
    }

    private void ShowFeedback(string message)
    {
        feedbackCancellation?.Cancel();
        feedbackCancellation = new CancellationTokenSource();
        ActionFeedbackText.Text = message;
        ActionFeedback.Opacity = 1;
        ActionFeedback.IsVisible = true;
        _ = AutoHideFeedbackAsync(feedbackCancellation.Token);
    }

    private async Task AutoHideFeedbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            ActionFeedback.Opacity = 0;
            await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken);
            ActionFeedback.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HideFeedback()
    {
        feedbackCancellation?.Cancel();
        ActionFeedback.IsVisible = false;
    }

    private void DismissFeedback_Click(object? sender, RoutedEventArgs e) => HideFeedback();

    private void ProjectSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (synchronizingProjectSearch || sender is not TextBox searchBox)
        {
            return;
        }

        var query = searchBox.Text;
        synchronizingProjectSearch = true;
        if (!ReferenceEquals(searchBox, ProjectSearchBox)) ProjectSearchBox.Text = query;
        if (!ReferenceEquals(searchBox, WideProjectSearchBox)) WideProjectSearchBox.Text = query;
        synchronizingProjectSearch = false;
        projectBrowser.Query = query;
    }

    private void SortProjects_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string field } || !Enum.TryParse<ProjectSortField>(field, out var parsed)) return;
        projectBrowser.ToggleSort(parsed);
    }

    private void RefreshProjectView()
    {
        projectBrowser.RefreshView();
    }

    private void UpdateSortButtonLabels()
    {
        SortNameButton.Content = SortLabel(Localization.Text.SortByName, ProjectSortField.Name);
        SortEngineButton.Content = SortLabel(Localization.Text.SortByEngine, ProjectSortField.Engine);
        SortLakonaButton.Content = SortLabel(Localization.Text.SortByLakona, ProjectSortField.Lakona);
        SortLastOpenedButton.Content = SortLabel(Localization.Text.SortByLastOpened, ProjectSortField.LastOpened);
        WideSortNameButton.Content = SortLabel(Localization.Text.ProjectName, ProjectSortField.Name);
        WideSortEngineButton.Content = SortLabel(Localization.Text.EngineVersion, ProjectSortField.Engine);
        WideSortLakonaButton.Content = SortLabel(Localization.Text.LakonaVersion, ProjectSortField.Lakona);
        WideSortLastOpenedButton.Content = SortLabel(Localization.Text.LastOpened, ProjectSortField.LastOpened);
    }

    private string SortLabel(string label, ProjectSortField field) =>
        field == projectBrowser.SortField ? $"{label} {(projectBrowser.SortDescending ? "↓" : "↑")}" : label;

    private void SwitchExperience(Control next)
    {
        if (ReferenceEquals(activeExperience, next)) return;
        experienceCancellation?.Cancel();
        experienceCancellation = new CancellationTokenSource();
        var previous = activeExperience;
        foreach (var experience in new Control[] { EmptyExperience, ProjectExperience, CreateExperience, SettingsExperience })
        {
            if (!ReferenceEquals(experience, previous) && !ReferenceEquals(experience, next))
                experience.IsVisible = false;
        }
        activeExperience = next;
        next.IsVisible = true;
        next.Opacity = 0;
        next.Opacity = 1;
        if (previous is not null) previous.Opacity = 0;
        _ = CompleteExperienceSwitchAsync(previous, experienceCancellation.Token);
    }

    private static async Task CompleteExperienceSwitchAsync(Control? previous, CancellationToken token)
    {
        if (previous is null) return;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(160), token);
            previous.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private string FormatEnvironmentSummary(
        IReadOnlyList<LocalApplicationInstallation> applications)
    {
        var names = applications
            .DistinctBy(application => application.Kind)
            .Select(application => application.DisplayName)
            .ToArray();
        return names.Length == 0
            ? Localization.Text.EnvironmentNone
            : Localization.Text.EnvironmentDetected(string.Join(Localization.Text.EnvironmentSeparator, names));
    }

    private static bool IsLaunchFailure(Exception exception) => exception is
        IOException or
        UnauthorizedAccessException or
        InvalidOperationException or
        ArgumentException or
        Win32Exception or
        PlatformNotSupportedException;

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                e.Handled = true;
                return;
            }
            BeginMoveDrag(e);
        }
    }

    private void ResizeGrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Normal &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
            sender is Border { Tag: string edgeName } &&
            Enum.TryParse<WindowEdge>(edgeName, out var edge))
        {
            BeginResizeDrag(edge, e);
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Normal)
        {
            restoredWindowSettings = CaptureWindowSettings();
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
