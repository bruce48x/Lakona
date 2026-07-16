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
    public static readonly ProjectCreationChoice Tuanjie = new("tuanjie", "Tuanjie");
    public static readonly ProjectCreationChoice Godot = new("godot", "Godot");
    public static readonly ProjectCreationChoice Console = new("console", "Console");

    private readonly HubLocalization localization;
    private string projectName = "MyGame";
    private string outputDirectory = DefaultOutputDirectory();
    private ProjectCreationChoice selectedClient = Unity;
    private ProjectCreationChoice? selectedClientVersion;
    private ProjectCreationChoice selectedTransport = null!;
    private ProjectCreationChoice selectedSerializer = null!;
    private ProjectCreationChoice selectedPersistence = null!;
    private ProjectCreationChoice selectedNuGetForUnitySource = null!;
    private ProjectCreationChoice selectedDeploymentProfile = null!;
    private bool isCreating;

    public ProjectCreationForm(HubLocalization? localization = null)
    {
        this.localization = localization ?? new HubLocalization();
        this.localization.PropertyChanged += Localization_PropertyChanged;
        RebuildLocalizedOptions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public HubText Text => localization.Text;

    public IReadOnlyList<ProjectCreationChoice> ClientOptions { get; private set; } = [];

    public ObservableCollection<ProjectCreationChoice> ClientVersionOptions { get; } = [];

    public IReadOnlyList<ProjectCreationChoice> TransportOptions { get; private set; } = [];

    public IReadOnlyList<ProjectCreationChoice> SerializerOptions { get; private set; } = [];

    public IReadOnlyList<ProjectCreationChoice> PersistenceOptions { get; private set; } = [];

    public IReadOnlyList<ProjectCreationChoice> NuGetForUnitySourceOptions { get; private set; } = [];

    public IReadOnlyList<ProjectCreationChoice> DeploymentProfileOptions { get; private set; } = [];

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
            if (value is null)
            {
                return;
            }

            var normalized = ClientOptions.FirstOrDefault(option => option.Id == value.Id) ?? value;
            if (SetField(ref selectedClient, normalized))
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
        set
        {
            if (value is not null)
            {
                SetField(ref selectedTransport, value);
            }
        }
    }

    public ProjectCreationChoice SelectedSerializer
    {
        get => selectedSerializer;
        set
        {
            if (value is not null)
            {
                SetField(ref selectedSerializer, value);
            }
        }
    }

    public ProjectCreationChoice SelectedPersistence
    {
        get => selectedPersistence;
        set
        {
            if (value is not null)
            {
                SetField(ref selectedPersistence, value);
            }
        }
    }

    public ProjectCreationChoice SelectedNuGetForUnitySource
    {
        get => selectedNuGetForUnitySource;
        set
        {
            if (value is not null)
            {
                SetField(ref selectedNuGetForUnitySource, value);
            }
        }
    }

    public ProjectCreationChoice SelectedDeploymentProfile
    {
        get => selectedDeploymentProfile;
        set
        {
            if (value is not null)
            {
                SetField(ref selectedDeploymentProfile, value);
            }
        }
    }

    public bool IsCreating
    {
        get => isCreating;
        set => SetField(ref isCreating, value);
    }

    public bool HasClientVersion => ClientVersionOptions.Count > 0;

    public bool UsesNuGetForUnity => SelectedClient.Id is "unity" or "tuanjie";

    public string ClientVersionHint => Text.ClientVersionHint(HasClientVersion);

    public string NuGetForUnityHint => Text.NuGetForUnityHint(UsesNuGetForUnity);

    public string TargetPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory) || string.IsNullOrWhiteSpace(ProjectName))
            {
                return Text.TargetPathMissing;
            }

            try
            {
                return Path.GetFullPath(Path.Combine(OutputDirectory.Trim(), ProjectName.Trim()));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Text.InvalidProjectPath;
            }
        }
    }

    public string ValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                return Text.ProjectNameRequired;
            }

            var name = ProjectName.Trim();
            if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return Text.InvalidProjectName;
            }

            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                return Text.OutputLocationRequired;
            }

            try
            {
                if (!Path.IsPathFullyQualified(Path.GetFullPath(OutputDirectory.Trim())))
                {
                    return Text.FullOutputPathRequired;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Text.InvalidOutputPath;
            }

            if (HasClientVersion && SelectedClientVersion is null)
            {
                return Text.ClientVersionRequired;
            }

            return Text.ConfigurationReady;
        }
    }

    public bool CanCreate => !IsCreating && ValidationMessage == Text.ConfigurationReady;

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

    internal HubCreationDraft CaptureDraft() => new(
        ProjectName,
        OutputDirectory,
        SelectedClient.Id,
        SelectedClientVersion?.Id,
        SelectedTransport.Id,
        SelectedSerializer.Id,
        SelectedPersistence.Id,
        SelectedNuGetForUnitySource.Id,
        SelectedDeploymentProfile.Id);

    internal void ApplyDraft(HubCreationDraft? draft)
    {
        if (draft is null)
        {
            return;
        }

        ProjectName = draft.ProjectName;
        OutputDirectory = draft.OutputDirectory;
        SelectedClient = ClientOptions.FirstOrDefault(option => option.Id == draft.ClientId) ?? Unity;
        SelectedClientVersion = ClientVersionOptions.FirstOrDefault(option => option.Id == draft.ClientVersionId)
                                ?? ClientVersionOptions.FirstOrDefault();
        SelectedTransport = TransportOptions.FirstOrDefault(option => option.Id == draft.TransportId) ?? SelectedTransport;
        SelectedSerializer = SerializerOptions.FirstOrDefault(option => option.Id == draft.SerializerId) ?? SelectedSerializer;
        SelectedPersistence = PersistenceOptions.FirstOrDefault(option => option.Id == draft.PersistenceId) ?? SelectedPersistence;
        SelectedNuGetForUnitySource = NuGetForUnitySourceOptions.FirstOrDefault(option => option.Id == draft.NuGetSourceId)
                                     ?? SelectedNuGetForUnitySource;
        SelectedDeploymentProfile = DeploymentProfileOptions.FirstOrDefault(option => option.Id == draft.DeploymentId)
                                    ?? SelectedDeploymentProfile;
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HubLocalization.Text))
        {
            RebuildLocalizedOptions();
        }
    }

    private void RebuildLocalizedOptions()
    {
        var clientId = selectedClient?.Id ?? Unity.Id;
        var clientVersionId = selectedClientVersion?.Id;
        var transportId = selectedTransport?.Id ?? "kcp";
        var serializerId = selectedSerializer?.Id ?? "memorypack";
        var persistenceId = selectedPersistence?.Id ?? "none";
        var nuGetSourceId = selectedNuGetForUnitySource?.Id ?? "embedded";
        var deploymentId = selectedDeploymentProfile?.Id ?? "none";

        ClientOptions =
        [
            new ProjectCreationChoice(Unity.Id, "Unity"),
            new ProjectCreationChoice(Tuanjie.Id, Text.Tuanjie),
            new ProjectCreationChoice(Godot.Id, "Godot"),
            new ProjectCreationChoice(Console.Id, "Console")
        ];
        TransportOptions = [new("tcp", "TCP"), new("websocket", "WebSocket"), new("kcp", "KCP")];
        SerializerOptions = [new("json", "JSON"), new("memorypack", "MemoryPack")];
        PersistenceOptions = [new("none", Text.NoDatabase), new("mysql", "MySQL"), new("postgres", "PostgreSQL")];
        NuGetForUnitySourceOptions = [new("embedded", Text.EmbeddedPackages), new("openupm", "OpenUPM")];
        DeploymentProfileOptions = [new("none", Text.NoDeploymentProfile), new("compose", "Docker Compose")];

        selectedClient = Find(ClientOptions, clientId);
        selectedTransport = Find(TransportOptions, transportId);
        selectedSerializer = Find(SerializerOptions, serializerId);
        selectedPersistence = Find(PersistenceOptions, persistenceId);
        selectedNuGetForUnitySource = Find(NuGetForUnitySourceOptions, nuGetSourceId);
        selectedDeploymentProfile = Find(DeploymentProfileOptions, deploymentId);

        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(ClientOptions));
        OnPropertyChanged(nameof(TransportOptions));
        OnPropertyChanged(nameof(SerializerOptions));
        OnPropertyChanged(nameof(PersistenceOptions));
        OnPropertyChanged(nameof(NuGetForUnitySourceOptions));
        OnPropertyChanged(nameof(DeploymentProfileOptions));
        OnPropertyChanged(nameof(SelectedClient));
        OnPropertyChanged(nameof(SelectedTransport));
        OnPropertyChanged(nameof(SelectedSerializer));
        OnPropertyChanged(nameof(SelectedPersistence));
        OnPropertyChanged(nameof(SelectedNuGetForUnitySource));
        OnPropertyChanged(nameof(SelectedDeploymentProfile));
        RefreshClientOptions(clientVersionId);
    }

    private void RefreshClientOptions(string? preferredVersionId = null)
    {
        ClientVersionOptions.Clear();
        foreach (var version in VersionsFor(SelectedClient.Id))
        {
            ClientVersionOptions.Add(version);
        }

        SelectedClientVersion = ClientVersionOptions.FirstOrDefault(version => version.Id == preferredVersionId)
                                ?? ClientVersionOptions.FirstOrDefault();
        if (!UsesNuGetForUnity)
        {
            SelectedNuGetForUnitySource = NuGetForUnitySourceOptions[0];
        }

        NotifyDerivedProperties();
    }

    private ProjectCreationChoice[] VersionsFor(string clientId) => clientId switch
    {
        "unity" => [new("2022", "Unity 2022 LTS"), new("6.0", "Unity 6.0"), new("6.3", "Unity 6.3")],
        "tuanjie" => [new("1.6.7", Text.TuanjieVersion)],
        "godot" => [new("4.6", "Godot 4.6")],
        "console" => [],
        _ => throw new InvalidOperationException($"Unsupported client: {clientId}")
    };

    private static ProjectCreationChoice Find(IReadOnlyList<ProjectCreationChoice> choices, string id) =>
        choices.First(choice => choice.Id == id);

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
