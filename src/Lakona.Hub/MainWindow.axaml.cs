using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lakona.Hub.Applications;
using Lakona.Hub.Updates;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed partial class MainWindow : Window
{
    private const string HelpIssuesUrl = "https://github.com/bruce48x/Lakona/issues";

    private readonly LakonaProjectInspector inspector = new();
    private readonly LakonaProjectCreator projectCreator = new();
    private readonly InstalledApplicationCatalog applicationCatalog;
    private readonly ManualApplicationStore manualApplicationStore;
    private readonly HubUserSettingsStore userSettingsStore;
    private readonly ApplicationLauncher applicationLauncher = new();
    private readonly IHubUpdateService updateService;
    private readonly List<ManualApplicationRegistration> manualApplicationRegistrations;
    private IReadOnlyList<LocalApplicationInstallation> automaticallyDetectedApplications = [];
    private IReadOnlyList<LocalApplicationInstallation> installedApplications = [];
    private bool isCreatingProject;
    private bool environmentDetectionComplete;
    private bool environmentDetectionFailed;
    private bool isUpdating;
    private bool hasCheckedForUpdates;
    private DateTimeOffset? lastUpdateCheckedAtUtc;
    private HubAvailableUpdate? availableUpdate;
    private HubPage currentPage = HubPage.Projects;
    private HubWindowSettings? restoredWindowSettings;

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
        InstalledApplicationCatalog applicationCatalog,
        ManualApplicationStore manualApplicationStore,
        HubUserSettingsStore userSettingsStore,
        HubUserSettings settings,
        bool enableStartupDetection)
    {
        Localization = localization;
        this.updateService = updateService;
        this.applicationCatalog = applicationCatalog;
        this.manualApplicationStore = manualApplicationStore;
        this.userSettingsStore = userSettingsStore;
        automaticallyDetectedApplications = RestoreDetectedApplications(settings.DetectedApplications);
        currentPage = settings.CurrentPage == "Settings" ? HubPage.Settings : HubPage.Projects;
        restoredWindowSettings = settings.Window;
        RestoreUpdateCheck(settings.UpdateCheck);
        manualApplicationRegistrations = manualApplicationStore.Load().ToList();
        ApplicationTools = [];
        CreationForm = new ProjectCreationForm(localization);
        CreationForm.ApplyDraft(settings.CreationDraft);
        InitializeComponent();
        RestoreWindow(settings.Window);
        DataContext = this;
        if (enableStartupDetection)
        {
            Opened += MainWindow_Opened;
        }
        PropertyChanged += MainWindow_PropertyChanged;
        Localization.PropertyChanged += Localization_PropertyChanged;
        CreationForm.PropertyChanged += CreationForm_PropertyChanged;
        Closing += MainWindow_Closing;
        ApplyApplications();
        RestoreProjects(settings.Projects);
        UpdateWindowFrame();
        UpdateEnvironmentTexts();
        UpdateUpdateTexts();
        UpdateExperience();
    }

    public ObservableCollection<ProjectListItem> Projects { get; } = [];

    public ObservableCollection<ApplicationToolItem> ApplicationTools { get; }

    public ProjectCreationForm CreationForm { get; }

    public HubLocalization Localization { get; }

    public string BundledDotNetSdkLabel => $".NET SDK {HubRuntimeInfo.BundledDotNetSdkVersion}";

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        await DetectApplicationsAsync(showFailureFeedback: true);
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
            var existing = Projects.FirstOrDefault(project =>
                string.Equals(project.Path, inspection.RootPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Projects.Remove(existing);
            }

            var item = ProjectListItem.FromInspection(inspection, installedApplications, Localization);
            ObserveProject(item);
            Projects.Insert(0, item);
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
        if (ProjectList.SelectedItem is ProjectListItem project)
        {
            ShowFeedback(Localization.Text.ProjectSelection(project.Name, project.StatusText, project.Path));
        }
    }

    private void OpenServer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProjectListItem project } ||
            project.SelectedServerEditor is not { } editor)
        {
            ShowFeedback(Localization.Text.NoSupportedIde);
            return;
        }

        try
        {
            applicationLauncher.Launch(ApplicationLaunchPlanner.OpenServer(project.Path, editor));
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
        if (sender is not Button { DataContext: ProjectListItem project } ||
            project.ClientApplication is not { } application)
        {
            ShowFeedback(Localization.Text.NoMatchingClientEditor);
            return;
        }

        try
        {
            applicationLauncher.Launch(ApplicationLaunchPlanner.OpenClient(
                project.Path,
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

    private void CreateProject_Click(object? sender, RoutedEventArgs e)
    {
        isCreatingProject = true;
        ActionFeedback.IsVisible = false;
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
        isCreatingProject = false;
        ActionFeedback.IsVisible = false;
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
            var result = await projectCreator.CreateAsync(CreationForm.CreateRequest());
            isCreatingProject = false;
            ShowInspection(inspector.Inspect(result.RootPath));
            ShowFeedback(Localization.Text.ProjectCreated(CreationForm.ProjectName.Trim()));
        }
        catch (Exception ex) when (ex is LakonaProjectCreationException or IOException or UnauthorizedAccessException)
        {
            ShowFeedback(Localization.Text.ProjectCreationFailed(ex.Message));
        }
        finally
        {
            CreationForm.IsCreating = false;
        }
    }

    private void Projects_Click(object? sender, RoutedEventArgs e)
    {
        currentPage = HubPage.Projects;
        isCreatingProject = false;
        ActionFeedback.IsVisible = false;
        UpdateExperience();
        TrySaveUserSettings();
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        currentPage = HubPage.Settings;
        isCreatingProject = false;
        ActionFeedback.IsVisible = false;
        UpdateExperience();
        TrySaveUserSettings();
    }

    private async void RefreshEnvironment_Click(object? sender, RoutedEventArgs e)
    {
        await DetectApplicationsAsync(showFailureFeedback: true);
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
        if (installedApplications.Any(application =>
                string.Equals(application.ExecutablePath, registration.ExecutablePath, StringComparison.OrdinalIgnoreCase)) ||
            manualApplicationRegistrations.Any(application =>
                string.Equals(application.ExecutablePath, registration.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        {
            ShowFeedback(Localization.Text.ApplicationAlreadyAdded(registration.DisplayName));
            return;
        }

        var updatedApplications = manualApplicationRegistrations.Append(registration).ToArray();
        try
        {
            manualApplicationStore.Save(updatedApplications);
            manualApplicationRegistrations.Clear();
            manualApplicationRegistrations.AddRange(updatedApplications);
            ApplyApplications();
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
        if (tool.ManualPath is not { } path)
        {
            return;
        }

        var updatedApplications = manualApplicationRegistrations
            .Where(application => !string.Equals(
                application.ExecutablePath,
                path,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        try
        {
            manualApplicationStore.Save(updatedApplications);
            manualApplicationRegistrations.Clear();
            manualApplicationRegistrations.AddRange(updatedApplications);
            ApplyApplications();
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
                availableUpdate = await updateService.CheckAsync();
                hasCheckedForUpdates = true;
                lastUpdateCheckedAtUtc = DateTimeOffset.UtcNow;
                TrySaveUserSettings();
                UpdateUpdateTexts();
                return;
            }

            UpdateStatusText.Text = Localization.Text.DownloadingSystemPackage(availableUpdate.Version);
            await updateService.PrepareAndLaunchAsync(availableUpdate);
            UpdateStatusText.Text = Localization.Text.SystemPackageInstallerOpened;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            UpdateStatusText.Text = Localization.Text.UpdateFailed(ex.Message);
        }
        finally
        {
            isUpdating = false;
            UpdateButton.IsEnabled = true;
        }
    }

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        ActionFeedback.IsVisible = false;
        HelpDialogOverlay.IsVisible = true;
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
        var onProjectsPage = currentPage == HubPage.Projects;
        var hasProjects = Projects.Count > 0;
        CreateExperience.IsVisible = onProjectsPage && isCreatingProject;
        EmptyExperience.IsVisible = onProjectsPage && !isCreatingProject && !hasProjects;
        ProjectExperience.IsVisible = onProjectsPage && !isCreatingProject && hasProjects;
        SettingsExperience.IsVisible = currentPage == HubPage.Settings;
        ProjectsNavButton.Classes.Set("selected", onProjectsPage);
        SettingsNavButton.Classes.Set("selected", currentPage == HubPage.Settings);
    }

    private async Task DetectApplicationsAsync(bool showFailureFeedback)
    {
        environmentDetectionComplete = false;
        environmentDetectionFailed = false;
        UpdateEnvironmentTexts();
        try
        {
            automaticallyDetectedApplications = await Task.Run(applicationCatalog.Detect);
            ApplyApplications();
            environmentDetectionComplete = true;
            TrySaveUserSettings();
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
            UpdateEnvironmentTexts();
        }
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HubLocalization.Text))
        {
            ActionFeedback.IsVisible = false;
            UpdateEnvironmentTexts();
            UpdateUpdateTexts();
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

    private void UpdateEnvironmentTexts()
    {
        var summary = environmentDetectionFailed
            ? Localization.Text.ToolDetectionFailed
            : environmentDetectionComplete
                ? FormatEnvironmentSummary(installedApplications)
                : Localization.Text.DetectingTools;
        EmptyEnvironmentSummaryText.Text = summary;
        ProjectEnvironmentSummaryText.Text = summary;
    }

    private void ApplyApplications()
    {
        var manuallyConfigured = manualApplicationRegistrations
            .Select(registration => SystemApplicationProbeSource.TryCreateInstallation(
                    registration.Kind,
                    registration.ExecutablePath,
                    out var installation)
                ? installation with { DisplayName = registration.DisplayName }
                : null)
            .OfType<LocalApplicationInstallation>()
            .ToArray();
        installedApplications = InstalledApplicationCatalog.MergePreferred(
            automaticallyDetectedApplications,
            manuallyConfigured);

        ApplicationTools.Clear();
        foreach (var tool in ApplicationToolList.Build(
                     installedApplications,
                     manualApplicationRegistrations,
                     Localization))
        {
            ApplicationTools.Add(tool);
        }

        foreach (var project in Projects)
        {
            project.RefreshApplications(installedApplications);
        }
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
                installedApplications,
                Localization,
                project.SelectedServerEditorPath,
                project.LastOpenedAtUtc);
            ObserveProject(item);
            Projects.Add(item);
        }
    }

    private string? TrySaveUserSettings()
    {
        try
        {
            userSettingsStore.Save(new HubUserSettings(
                Localization.Language,
                Projects.Select(project => new HubProjectSettings(
                    project.Path,
                    project.SelectedServerEditor?.ExecutablePath,
                    project.LastOpenedAtUtc)).ToArray(),
                automaticallyDetectedApplications.Select(application => new HubDetectedApplicationSettings(
                    application.Kind.ToString(),
                    application.DisplayName,
                    application.ExecutablePath,
                    application.Version)).ToArray(),
                CreationForm.CaptureDraft(),
                currentPage.ToString(),
                CaptureWindowSettings(),
                CaptureUpdateCheck()));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Localization.Text.UserSettingsSaveFailed(ex.Message);
        }
    }

    private static HubStartupSettings LoadStartupSettings()
    {
        var store = new HubUserSettingsStore();
        var detectedLanguage = HubLocalization.DetectLanguage(CultureInfo.CurrentUICulture);
        return new HubStartupSettings(store, store.Load(detectedLanguage));
    }

    private sealed record HubStartupSettings(HubUserSettingsStore Store, HubUserSettings Settings);

    private void ObserveProject(ProjectListItem project) => project.PropertyChanged += Project_PropertyChanged;

    private void Project_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectListItem.SelectedServerEditor) or nameof(ProjectListItem.LastOpened))
        {
            TrySaveUserSettings();
        }
    }

    private void CreationForm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectCreationForm.ProjectName) or
            nameof(ProjectCreationForm.OutputDirectory) or
            nameof(ProjectCreationForm.SelectedClient) or
            nameof(ProjectCreationForm.SelectedClientVersion) or
            nameof(ProjectCreationForm.SelectedTransport) or
            nameof(ProjectCreationForm.SelectedSerializer) or
            nameof(ProjectCreationForm.SelectedPersistence) or
            nameof(ProjectCreationForm.SelectedNuGetForUnitySource) or
            nameof(ProjectCreationForm.SelectedDeploymentProfile))
        {
            TrySaveUserSettings();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e) => TrySaveUserSettings();

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

    private void UpdateWindowFrame() =>
        WindowFrame.Classes.Set("maximized", WindowState == WindowState.Maximized);

    private void ShowFeedback(string message)
    {
        ActionFeedbackText.Text = message;
        ActionFeedback.IsVisible = true;
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
    {
        if (WindowState == WindowState.Normal)
        {
            restoredWindowSettings = CaptureWindowSettings();
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private enum HubPage
    {
        Projects,
        Settings
    }
}
