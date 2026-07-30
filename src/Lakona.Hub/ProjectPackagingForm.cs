using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed record ProjectPackagingChoice(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class ProjectPackagingForm : INotifyPropertyChanged, IDisposable
{
    private readonly string projectRoot;
    private readonly string? dotNetExecutablePath;
    private readonly ILakonaProjectPackager packager;
    private readonly HubLocalization localization;
    private ProjectPackagingChoice selectedKind = null!;
    private ProjectPackagingChoice selectedRuntime = null!;
    private ProjectPackagingChoice selectedConfiguration = null!;
    private CancellationTokenSource? packagingCancellation;
    private bool isPackaging;
    private string statusText = "";
    private string? artifactPath;

    public ProjectPackagingForm(
        string projectRoot,
        string? dotNetExecutablePath,
        ILakonaProjectPackager? packager = null,
        HubLocalization? localization = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        this.projectRoot = Path.GetFullPath(projectRoot);
        this.dotNetExecutablePath = dotNetExecutablePath;
        this.packager = packager ?? new LakonaProjectPackager();
        this.localization = localization ?? new HubLocalization();
        this.localization.PropertyChanged += Localization_PropertyChanged;
        RebuildLocalizedOptions();
        statusText = CanPackage ? Text.PackageReady : Text.PackageSdkRequired;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HubText Text => localization.Text;

    public string ProjectName => Path.GetFileName(projectRoot);

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
            DotNetExecutablePath: dotNetExecutablePath);
    }

    public async Task PackageAsync(CancellationToken cancellationToken = default)
    {
        var request = CreateRequest();
        packagingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsPackaging = true;
        ArtifactPath = null;
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
        }
        catch (OperationCanceledException) when (packagingCancellation.IsCancellationRequested)
        {
            StatusText = Text.PackageCanceled;
        }
        catch (Exception exception)
        {
            StatusText = Text.PackageFailed(exception.Message);
        }
        finally
        {
            packagingCancellation.Dispose();
            packagingCancellation = null;
            IsPackaging = false;
        }
    }

    public void Cancel() => packagingCancellation?.Cancel();

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
