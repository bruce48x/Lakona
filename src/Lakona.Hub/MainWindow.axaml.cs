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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Opened += MainWindow_Opened;
        PropertyChanged += MainWindow_PropertyChanged;
        UpdateWindowFrame();
        UpdateExperience();
    }

    public ObservableCollection<ProjectListItem> Projects { get; } = [];

    public ProjectCreationForm CreationForm { get; } = new();

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        try
        {
            installedApplications = await Task.Run(applicationCatalog.Detect);
            foreach (var project in Projects)
            {
                project.RefreshApplications(installedApplications);
            }

            var summary = FormatEnvironmentSummary(installedApplications);
            EmptyEnvironmentSummaryText.Text = summary;
            ProjectEnvironmentSummaryText.Text = summary;
        }
        catch (Exception ex)
        {
            EmptyEnvironmentSummaryText.Text = "本机开发工具识别失败";
            ProjectEnvironmentSummaryText.Text = "本机开发工具识别失败";
            ShowFeedback($"无法识别本机开发工具：{ex.Message}");
        }
    }

    private async void ImportProject_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 Lakona 项目目录",
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

            Projects.Insert(0, ProjectListItem.FromInspection(inspection, installedApplications));
            UpdateExperience();
        }

        ShowFeedback(inspection.Status switch
        {
            LakonaProjectStatus.Ready => $"已导入“{inspection.Name}”。Hub 只读取了项目结构，没有写入任何管理文件。",
            LakonaProjectStatus.Incomplete => $"“{inspection.Name}”已加入列表，但项目结构需要检查：{inspection.Diagnostics.Count} 项提示。",
            LakonaProjectStatus.NotLakonaProject => "所选目录不是可识别的 Lakona 项目。项目内容没有被修改。",
            LakonaProjectStatus.NotFound => "所选项目目录不存在。",
            _ => "无法识别该项目。"
        });
    }

    private void ProjectList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ProjectList.SelectedItem is ProjectListItem project)
        {
            ShowFeedback($"{project.Name}：{project.StatusText}。路径：{project.Path}");
        }
    }

    private void OpenServer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProjectListItem project } ||
            project.SelectedServerEditor is not { } editor)
        {
            ShowFeedback("未检测到 Rider、Visual Studio 或 VS Code。请先安装一个受支持的 IDE。");
            return;
        }

        try
        {
            applicationLauncher.Launch(ApplicationLaunchPlanner.OpenServer(project.Path, editor));
            project.MarkOpened();
            ShowFeedback($"正在使用 {editor.DisplayName} 打开“{project.Name}”的服务端。");
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            ShowFeedback($"无法打开服务端：{ex.Message}");
        }
    }

    private void OpenClient_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProjectListItem project } ||
            project.ClientApplication is not { } application)
        {
            ShowFeedback("没有检测到与当前项目客户端匹配的 Unity 或 Godot 编辑器。");
            return;
        }

        try
        {
            applicationLauncher.Launch(ApplicationLaunchPlanner.OpenClient(
                project.Path,
                project.ClientKind,
                application));
            project.MarkOpened();
            ShowFeedback($"正在使用 {application.DisplayName} 打开“{project.Name}”的客户端。");
        }
        catch (Exception ex) when (IsLaunchFailure(ex))
        {
            ShowFeedback($"无法打开客户端：{ex.Message}");
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
            Title = "选择新项目的保存位置",
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
        ShowFeedback($"正在创建“{CreationForm.ProjectName.Trim()}”…");
        try
        {
            var result = await projectCreator.CreateAsync(CreationForm.CreateRequest());
            isCreatingProject = false;
            ShowInspection(inspector.Inspect(result.RootPath));
            ShowFeedback($"已创建“{CreationForm.ProjectName.Trim()}”。项目生成逻辑与 lakona-tool 完全共享。");
        }
        catch (Exception ex) when (ex is LakonaProjectCreationException or IOException or UnauthorizedAccessException)
        {
            ShowFeedback($"创建项目失败：{ex.Message}");
        }
        finally
        {
            CreationForm.IsCreating = false;
        }
    }

    private void Environment_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeedback($".NET 10 环境已就绪。{FormatEnvironmentSummary(installedApplications)}。");
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeedback("默认服务端 IDE 按 Rider、Visual Studio、VS Code 的顺序选择，也可以在每个项目行中切换。");
    }

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        ShowFeedback("帮助与反馈入口将在发布准备阶段接入。");
    }

    private void UpdateExperience()
    {
        var hasProjects = Projects.Count > 0;
        CreateExperience.IsVisible = isCreatingProject;
        EmptyExperience.IsVisible = !isCreatingProject && !hasProjects;
        ProjectExperience.IsVisible = !isCreatingProject && hasProjects;
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

    private static string FormatEnvironmentSummary(
        IReadOnlyList<LocalApplicationInstallation> applications)
    {
        var names = applications
            .DistinctBy(application => application.Kind)
            .Select(application => application.DisplayName)
            .ToArray();
        return names.Length == 0
            ? "未识别 Rider、Visual Studio、VS Code、Unity 或 Godot"
            : $"已识别 {string.Join("、", names)}";
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
}
