using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lakona.Hub.Applications;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed partial class MainWindow : Window
{
    private readonly LakonaProjectInspector inspector = new();
    private readonly LakonaProjectCreator projectCreator = new();
    private readonly InstalledApplicationCatalog applicationCatalog = new();
    private readonly ApplicationLauncher applicationLauncher = new();
    private IReadOnlyList<LocalApplicationInstallation> installedApplications = [];
    private bool isCreatingProject;
    private bool environmentDetectionComplete;
    private bool environmentDetectionFailed;
    private HubPage currentPage = HubPage.Projects;

    public MainWindow()
        : this(new HubLocalization())
    {
    }

    internal MainWindow(HubLocalization localization)
    {
        Localization = localization;
        CreationForm = new ProjectCreationForm(localization);
        InitializeComponent();
        DataContext = this;
        Opened += MainWindow_Opened;
        PropertyChanged += MainWindow_PropertyChanged;
        Localization.PropertyChanged += Localization_PropertyChanged;
        UpdateWindowFrame();
        UpdateEnvironmentTexts();
        UpdateExperience();
    }

    public ObservableCollection<ProjectListItem> Projects { get; } = [];

    public ProjectCreationForm CreationForm { get; }

    public HubLocalization Localization { get; }

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
        if (inspection.Status is LakonaProjectStatus.Ready or LakonaProjectStatus.Incomplete)
        {
            var existing = Projects.FirstOrDefault(project =>
                string.Equals(project.Path, inspection.RootPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Projects.Remove(existing);
            }

            Projects.Insert(0, ProjectListItem.FromInspection(inspection, installedApplications, Localization));
            UpdateExperience();
        }

        ShowFeedback(inspection.Status switch
        {
            LakonaProjectStatus.Ready => Localization.Text.Imported(inspection.Name),
            LakonaProjectStatus.Incomplete => Localization.Text.ImportedIncomplete(inspection.Name, inspection.Diagnostics.Count),
            LakonaProjectStatus.NotLakonaProject => Localization.Text.NotLakonaProject,
            LakonaProjectStatus.NotFound => Localization.Text.ProjectNotFound,
            _ => Localization.Text.ProjectUnrecognized
        });
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
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        currentPage = HubPage.Settings;
        isCreatingProject = false;
        ActionFeedback.IsVisible = false;
        UpdateExperience();
    }

    private async void RefreshEnvironment_Click(object? sender, RoutedEventArgs e)
    {
        await DetectApplicationsAsync(showFailureFeedback: true);
    }

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeedback(Localization.Text.HelpComingSoon);
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
            installedApplications = await Task.Run(applicationCatalog.Detect);
            foreach (var project in Projects)
            {
                project.RefreshApplications(installedApplications);
            }

            environmentDetectionComplete = true;
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
        SettingsEnvironmentSummaryText.Text = summary;
    }

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
        Win32Exception;

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    private enum HubPage
    {
        Projects,
        Settings
    }
}
