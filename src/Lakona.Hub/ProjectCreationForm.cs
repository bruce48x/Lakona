using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed record ProjectCreationChoice(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class ProjectCreationForm : INotifyPropertyChanged
{
    public static readonly ProjectCreationChoice Unity = new("unity", "Unity");
    public static readonly ProjectCreationChoice Tuanjie = new("tuanjie", "团结引擎");
    public static readonly ProjectCreationChoice Godot = new("godot", "Godot");
    public static readonly ProjectCreationChoice Console = new("console", "Console");

    private static readonly IReadOnlyDictionary<string, ProjectCreationChoice[]> Versions =
        new Dictionary<string, ProjectCreationChoice[]>(StringComparer.Ordinal)
        {
            [Unity.Id] = [new("2022", "Unity 2022 LTS"), new("6.0", "Unity 6.0"), new("6.3", "Unity 6.3")],
            [Tuanjie.Id] = [new("1.6.7", "团结引擎 1.6.7")],
            [Godot.Id] = [new("4.6", "Godot 4.6")],
            [Console.Id] = []
        };

    private string projectName = "MyGame";
    private string outputDirectory = DefaultOutputDirectory();
    private ProjectCreationChoice selectedClient = Unity;
    private ProjectCreationChoice? selectedClientVersion;
    private ProjectCreationChoice selectedTransport;
    private ProjectCreationChoice selectedSerializer;
    private ProjectCreationChoice selectedPersistence;
    private ProjectCreationChoice selectedNuGetForUnitySource;
    private ProjectCreationChoice selectedDeploymentProfile;
    private bool isCreating;

    public ProjectCreationForm()
    {
        selectedTransport = TransportOptions[2];
        selectedSerializer = SerializerOptions[1];
        selectedPersistence = PersistenceOptions[0];
        selectedNuGetForUnitySource = NuGetForUnitySourceOptions[0];
        selectedDeploymentProfile = DeploymentProfileOptions[0];
        RefreshClientOptions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ProjectCreationChoice> ClientOptions { get; } = [Unity, Tuanjie, Godot, Console];

    public ObservableCollection<ProjectCreationChoice> ClientVersionOptions { get; } = [];

    public IReadOnlyList<ProjectCreationChoice> TransportOptions { get; } =
        [new("tcp", "TCP"), new("websocket", "WebSocket"), new("kcp", "KCP")];

    public IReadOnlyList<ProjectCreationChoice> SerializerOptions { get; } =
        [new("json", "JSON"), new("memorypack", "MemoryPack")];

    public IReadOnlyList<ProjectCreationChoice> PersistenceOptions { get; } =
        [new("none", "不使用数据库"), new("mysql", "MySQL"), new("postgres", "PostgreSQL")];

    public IReadOnlyList<ProjectCreationChoice> NuGetForUnitySourceOptions { get; } =
        [new("embedded", "内置包源"), new("openupm", "OpenUPM")];

    public IReadOnlyList<ProjectCreationChoice> DeploymentProfileOptions { get; } =
        [new("none", "不生成部署配置"), new("compose", "Docker Compose")];

    public string ProjectName
    {
        get => projectName;
        set => SetField(ref projectName, value);
    }

    public string OutputDirectory
    {
        get => outputDirectory;
        set => SetField(ref outputDirectory, value);
    }

    public ProjectCreationChoice SelectedClient
    {
        get => selectedClient;
        set
        {
            if (SetField(ref selectedClient, value))
            {
                RefreshClientOptions();
            }
        }
    }

    public ProjectCreationChoice? SelectedClientVersion
    {
        get => selectedClientVersion;
        set => SetField(ref selectedClientVersion, value);
    }

    public ProjectCreationChoice SelectedTransport
    {
        get => selectedTransport;
        set => SetField(ref selectedTransport, value);
    }

    public ProjectCreationChoice SelectedSerializer
    {
        get => selectedSerializer;
        set => SetField(ref selectedSerializer, value);
    }

    public ProjectCreationChoice SelectedPersistence
    {
        get => selectedPersistence;
        set => SetField(ref selectedPersistence, value);
    }

    public ProjectCreationChoice SelectedNuGetForUnitySource
    {
        get => selectedNuGetForUnitySource;
        set => SetField(ref selectedNuGetForUnitySource, value);
    }

    public ProjectCreationChoice SelectedDeploymentProfile
    {
        get => selectedDeploymentProfile;
        set => SetField(ref selectedDeploymentProfile, value);
    }

    public bool IsCreating
    {
        get => isCreating;
        set => SetField(ref isCreating, value);
    }

    public bool HasClientVersion => ClientVersionOptions.Count > 0;

    public bool UsesNuGetForUnity => SelectedClient.Id is "unity" or "tuanjie";

    public string ClientVersionHint => HasClientVersion ? "选择客户端使用的编辑器版本" : "Console 客户端不需要引擎版本";

    public string NuGetForUnityHint => UsesNuGetForUnity
        ? "Unity 系客户端的包获取方式"
        : "当前客户端不使用 NuGetForUnity";

    public string TargetPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory) || string.IsNullOrWhiteSpace(ProjectName))
            {
                return "请填写项目名称和保存位置";
            }

            try
            {
                return Path.GetFullPath(Path.Combine(OutputDirectory.Trim(), ProjectName.Trim()));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "项目路径无效";
            }
        }
    }

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                return "请输入项目名称";
            }

            var name = ProjectName.Trim();
            if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "项目名称包含不能用于文件夹的字符";
            }

            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                return "请选择保存位置";
            }

            try
            {
                if (!Path.IsPathFullyQualified(Path.GetFullPath(OutputDirectory.Trim())))
                {
                    return "请选择完整的保存路径";
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "保存路径无效";
            }

            if (HasClientVersion && SelectedClientVersion is null)
            {
                return "请选择客户端版本";
            }

            return "配置完整，可以继续创建";
        }
    }

    public bool CanCreate => !IsCreating && ValidationMessage == "配置完整，可以继续创建";

    public LakonaProjectCreationRequest CreateRequest()
    {
        if (!CanCreate)
        {
            throw new InvalidOperationException(ValidationMessage);
        }

        return new LakonaProjectCreationRequest(
            ProjectName.Trim(),
            OutputDirectory.Trim(),
            SelectedClient.Id switch
            {
                "unity" => LakonaClientEngine.Unity,
                "tuanjie" => LakonaClientEngine.Tuanjie,
                "godot" => LakonaClientEngine.Godot,
                "console" => LakonaClientEngine.Console,
                _ => throw new InvalidOperationException($"Unsupported client: {SelectedClient.Id}")
            },
            SelectedClientVersion?.Id switch
            {
                "2022" => LakonaClientEngineVersion.Unity2022,
                "6.0" => LakonaClientEngineVersion.Unity60,
                "6.3" => LakonaClientEngineVersion.Unity63,
                "1.6.7" => LakonaClientEngineVersion.Tuanjie167,
                "4.6" => LakonaClientEngineVersion.Godot46,
                null => null,
                var value => throw new InvalidOperationException($"Unsupported client version: {value}")
            },
            SelectedTransport.Id switch
            {
                "tcp" => LakonaTransport.Tcp,
                "websocket" => LakonaTransport.WebSocket,
                "kcp" => LakonaTransport.Kcp,
                _ => throw new InvalidOperationException($"Unsupported transport: {SelectedTransport.Id}")
            },
            SelectedSerializer.Id == "json" ? LakonaSerializer.Json : LakonaSerializer.MemoryPack,
            SelectedPersistence.Id switch
            {
                "mysql" => LakonaPersistence.MySql,
                "postgres" => LakonaPersistence.Postgres,
                _ => LakonaPersistence.None
            },
            SelectedNuGetForUnitySource.Id == "openupm"
                ? LakonaNuGetForUnitySource.OpenUpm
                : LakonaNuGetForUnitySource.Embedded,
            SelectedDeploymentProfile.Id == "compose"
                ? LakonaDeploymentProfile.Compose
                : LakonaDeploymentProfile.None);
    }

    private void RefreshClientOptions()
    {
        ClientVersionOptions.Clear();
        foreach (var version in Versions[SelectedClient.Id])
        {
            ClientVersionOptions.Add(version);
        }

        SelectedClientVersion = ClientVersionOptions.FirstOrDefault();
        if (!UsesNuGetForUnity)
        {
            SelectedNuGetForUnitySource = NuGetForUnitySourceOptions[0];
        }

        NotifyDerivedProperties();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        NotifyDerivedProperties();
        return true;
    }

    private void NotifyDerivedProperties()
    {
        OnPropertyChanged(nameof(TargetPath));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(HasClientVersion));
        OnPropertyChanged(nameof(UsesNuGetForUnity));
        OnPropertyChanged(nameof(ClientVersionHint));
        OnPropertyChanged(nameof(NuGetForUnityHint));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string DefaultOutputDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(string.IsNullOrWhiteSpace(documents) ? Environment.CurrentDirectory : documents, "Lakona Projects");
    }
}
