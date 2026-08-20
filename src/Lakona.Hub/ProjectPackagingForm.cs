using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed record ProjectPackagingChoice(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public interface IArtifactFolderLauncher
{
    void OpenContainingFolder(string artifactPath);
}

internal interface IPackagingLogStore
{
    string WriteFailureLog(string contents);
}

internal sealed class FilePackagingLogStore : IPackagingLogStore
{
    private const int MaxRetainedLogs = 20;
    private readonly string logDirectory;

    public FilePackagingLogStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lakona",
            "Hub",
            "logs"))
    {
    }

    internal FilePackagingLogStore(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        this.logDirectory = Path.GetFullPath(logDirectory);
    }

    public string WriteFailureLog(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(
            logDirectory,
            $"package-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, contents, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        PruneOldLogs(path);
        return path;
    }

    private void PruneOldLogs(string currentPath)
    {
        foreach (var oldLog in Directory
                     .EnumerateFiles(logDirectory, "package-*.log")
                     .Where(path => !StringComparer.Ordinal.Equals(path, currentPath))
                     .OrderByDescending(File.GetCreationTimeUtc)
                     .Skip(MaxRetainedLogs - 1))
        {
            try
            {
                File.Delete(oldLog);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A stale log must not hide the current packaging failure.
            }
        }
    }
}

public sealed class SystemArtifactFolderLauncher : IArtifactFolderLauncher
{
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public SystemArtifactFolderLauncher()
        : this(Process.Start)
    {
    }

    internal SystemArtifactFolderLauncher(Func<ProcessStartInfo, Process?> startProcess)
    {
        ArgumentNullException.ThrowIfNull(startProcess);
        this.startProcess = startProcess;
    }

    public void OpenContainingFolder(string artifactPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(artifactPath));
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                $"The package artifact directory does not exist: {directory}");
        }

        _ = startProcess(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
}

public sealed class ProjectPackagingForm : INotifyPropertyChanged, IDisposable
{
    private readonly string projectRoot;
    private readonly string? dotNetExecutablePath;
    private readonly ILakonaProjectPackager packager;
    private readonly HubLocalization localization;
    private readonly IArtifactFolderLauncher artifactFolderLauncher;
    private readonly IPackagingLogStore packagingLogStore;
    private ProjectPackagingChoice selectedKind = null!;
    private ProjectPackagingChoice selectedRuntime = null!;
    private ProjectPackagingChoice selectedConfiguration = null!;
    private CancellationTokenSource? packagingCancellation;
    private bool isPackaging;
    private string outputDirectory;
    private string statusText = "";
    private string? artifactPath;
    private string? failureLogPath;

    public ProjectPackagingForm(
        string projectRoot,
        string? dotNetExecutablePath,
        ILakonaProjectPackager? packager = null,
        HubLocalization? localization = null,
        IArtifactFolderLauncher? artifactFolderLauncher = null)
        : this(
            projectRoot,
            dotNetExecutablePath,
            packager ?? new LakonaProjectPackager(),
            localization ?? new HubLocalization(),
            artifactFolderLauncher ?? new SystemArtifactFolderLauncher(),
            new FilePackagingLogStore())
    {
    }

    internal ProjectPackagingForm(
        string projectRoot,
        string? dotNetExecutablePath,
        ILakonaProjectPackager packager,
        HubLocalization localization,
        IArtifactFolderLauncher artifactFolderLauncher,
        IPackagingLogStore packagingLogStore)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(packager);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(artifactFolderLauncher);
        ArgumentNullException.ThrowIfNull(packagingLogStore);
        this.projectRoot = Path.GetFullPath(projectRoot);
        this.dotNetExecutablePath = dotNetExecutablePath;
        this.packager = packager;
        this.localization = localization;
        this.artifactFolderLauncher = artifactFolderLauncher;
        this.packagingLogStore = packagingLogStore;
        BuildTag = new LakonaProjectInspector().Inspect(this.projectRoot).BuildTag ?? "";
        outputDirectory = Path.Combine(this.projectRoot, "Server", "Build");
        this.localization.PropertyChanged += Localization_PropertyChanged;
        RebuildLocalizedOptions();
        statusText = CanPackage ? Text.PackageReady : Text.PackageSdkRequired;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HubText Text => localization.Text;

    public string ProjectName => Path.GetFileName(projectRoot);

    public string BuildTag { get; }

    public IReadOnlyList<ProjectPackagingChoice> KindOptions { get; private set; } = [];

    public IReadOnlyList<ProjectPackagingChoice> RuntimeOptions { get; private set; } = [];

    public IReadOnlyList<ProjectPackagingChoice> ConfigurationOptions { get; private set; } = [];

    public ProjectPackagingChoice SelectedKind
    {
        get => selectedKind;
        set
        {
            if (value is not null && SetField(ref selectedKind, value))
            {
                OnPropertyChanged(nameof(ShowsRuntime));
            }
        }
    }

    public ProjectPackagingChoice SelectedRuntime
    {
        get => selectedRuntime;
        set
        {
            if (value is not null)
            {
                SetField(ref selectedRuntime, value);
            }
        }
    }

    public ProjectPackagingChoice SelectedConfiguration
    {
        get => selectedConfiguration;
        set
        {
            if (value is not null)
            {
                SetField(ref selectedConfiguration, value);
            }
        }
    }

    public bool ShowsRuntime => SelectedKind.Id == "server";

    public bool IsPackaging
    {
        get => isPackaging;
        private set
        {
            if (SetField(ref isPackaging, value))
            {
                OnPropertyChanged(nameof(CanPackage));
                OnPropertyChanged(nameof(CanClose));
            }
        }
    }

    public bool CanPackage => !IsPackaging && !string.IsNullOrWhiteSpace(dotNetExecutablePath);

    public bool CanClose => !IsPackaging;

    public string OutputDirectory
    {
        get => outputDirectory;
        set => SetField(ref outputDirectory, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string? ArtifactPath
    {
        get => artifactPath;
        private set
        {
            if (SetField(ref artifactPath, value))
            {
                OnPropertyChanged(nameof(HasArtifact));
            }
        }
    }

    public bool HasArtifact => !string.IsNullOrWhiteSpace(ArtifactPath);

    public string? FailureLogPath
    {
        get => failureLogPath;
        private set
        {
            if (SetField(ref failureLogPath, value))
            {
                OnPropertyChanged(nameof(HasFailureLog));
            }
        }
    }

    public bool HasFailureLog => !string.IsNullOrWhiteSpace(FailureLogPath);

    public LakonaPackageRequest CreateRequest()
    {
        if (!CanPackage)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(dotNetExecutablePath)
                    ? Text.PackageSdkRequired
                    : Text.PackageAlreadyRunning);
        }

        return new LakonaPackageRequest(
            projectRoot,
            SelectedKind.Id == "server"
                ? LakonaPackageKind.Server
                : LakonaPackageKind.Hotfix,
            ShowsRuntime ? SelectedRuntime.Id : null,
            SelectedConfiguration.Id,
            OutputDirectory: OutputDirectory.Trim(),
            DotNetExecutablePath: dotNetExecutablePath);
    }

    public async Task PackageAsync(CancellationToken cancellationToken = default)
    {
        var request = CreateRequest();
        packagingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsPackaging = true;
        ArtifactPath = null;
        FailureLogPath = null;
        StatusText = Text.PackagingStarted;
        try
        {
            var progress = new CallbackProgress<LakonaPackageProgress>(UpdateProgress);
            var result = await packager.PackAsync(
                request,
                progress,
                packagingCancellation.Token);
            ArtifactPath = result.ArtifactPath;
            StatusText = Text.PackageSucceeded;
            artifactFolderLauncher.OpenContainingFolder(result.ArtifactPath);
        }
        catch (OperationCanceledException) when (packagingCancellation.IsCancellationRequested)
        {
            StatusText = Text.PackageCanceled;
        }
        catch (Exception exception)
        {
            StatusText = Text.PackageFailed(FirstLine(exception.Message));
            try
            {
                FailureLogPath = packagingLogStore.WriteFailureLog(exception.ToString());
            }
            catch (Exception logException) when (logException is IOException or UnauthorizedAccessException)
            {
                FailureLogPath = null;
            }
        }
        finally
        {
            packagingCancellation.Dispose();
            packagingCancellation = null;
            IsPackaging = false;
        }
    }

    public void Cancel() => packagingCancellation?.Cancel();

    public void OpenFailureLogFolder()
    {
        if (FailureLogPath is not { } path)
        {
            return;
        }

        artifactFolderLauncher.OpenContainingFolder(path);
    }

    public void Dispose()
    {
        localization.PropertyChanged -= Localization_PropertyChanged;
        packagingCancellation?.Cancel();
        packagingCancellation?.Dispose();
        packagingCancellation = null;
    }

    private void UpdateProgress(LakonaPackageProgress progress)
    {
        StatusText = progress.Stage switch
        {
            LakonaPackageStage.Validating => Text.PackageValidating,
            LakonaPackageStage.Building => Text.PackageBuilding,
            LakonaPackageStage.Completed => Text.PackageSucceeded,
            _ => Text.PackagingStarted
        };
    }

    private static string FirstLine(string message)
    {
        using var reader = new StringReader(message);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return message.Trim();
    }

    private void RebuildLocalizedOptions()
    {
        var kindId = selectedKind?.Id ?? "server";
        var runtimeId = selectedRuntime?.Id ?? "linux-x64";
        var configurationId = selectedConfiguration?.Id ?? "Release";

        KindOptions =
        [
            new ProjectPackagingChoice("server", Text.ServerPackage),
            new ProjectPackagingChoice("hotfix", Text.HotfixPackage)
        ];
        RuntimeOptions =
        [
            new ProjectPackagingChoice("linux-x64", "Linux x64"),
            new ProjectPackagingChoice("linux-arm64", "Linux ARM64"),
            new ProjectPackagingChoice("win-x64", "Windows x64"),
            new ProjectPackagingChoice("win-arm64", "Windows ARM64")
        ];
        ConfigurationOptions =
        [
            new ProjectPackagingChoice("Release", "Release"),
            new ProjectPackagingChoice("Debug", "Debug")
        ];

        selectedKind = KindOptions.Single(option => option.Id == kindId);
        selectedRuntime = RuntimeOptions.Single(option => option.Id == runtimeId);
        selectedConfiguration = ConfigurationOptions.Single(option => option.Id == configurationId);
        OnPropertyChanged(nameof(KindOptions));
        OnPropertyChanged(nameof(RuntimeOptions));
        OnPropertyChanged(nameof(ConfigurationOptions));
        OnPropertyChanged(nameof(SelectedKind));
        OnPropertyChanged(nameof(SelectedRuntime));
        OnPropertyChanged(nameof(SelectedConfiguration));
        OnPropertyChanged(nameof(ShowsRuntime));
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HubLocalization.Text) or nameof(HubLocalization.SelectedLanguage))
        {
            RebuildLocalizedOptions();
            OnPropertyChanged(nameof(Text));
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
