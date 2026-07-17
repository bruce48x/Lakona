using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Lakona.Hub.Applications;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed class ProjectListItem : INotifyPropertyChanged
{
    private readonly HubLocalization localization;
    private readonly string? inspectedName;
    private readonly string? inspectedLakonaVersion;
    private readonly LakonaProjectStatus inspectionStatus;
    private readonly TimeProvider timeProvider;
    private LocalApplicationInstallation? selectedServerEditor;
    private LocalApplicationInstallation? clientApplication;
    private string? preferredServerEditorPath;
    private DateTimeOffset? lastOpenedAtUtc;

    private ProjectListItem(
        LakonaProjectInspection inspection,
        HubLocalization localization,
        string? preferredServerEditorPath,
        DateTimeOffset? lastOpenedAtUtc,
        TimeProvider timeProvider)
    {
        this.localization = localization;
        inspectedName = inspection.Name;
        inspectedLakonaVersion = inspection.LakonaVersion;
        inspectionStatus = inspection.Status;
        localization.PropertyChanged += Localization_PropertyChanged;
        Path = inspection.RootPath;
        ServerPath = inspection.ServerPath ?? System.IO.Path.Combine(Path, "Server");
        ClientPath = inspection.ClientPath ?? System.IO.Path.Combine(Path, "Client");
        ClientKind = inspection.Client;
        ClientVersion = inspection.ClientVersion;
        this.preferredServerEditorPath = preferredServerEditorPath;
        this.lastOpenedAtUtc = lastOpenedAtUtc;
        this.timeProvider = timeProvider;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private HubText Text => localization.Text;

    public string Name => string.IsNullOrWhiteSpace(inspectedName) ? Text.UnnamedProject : inspectedName;

    public string Path { get; }

    public string ServerPath { get; }

    public string ClientPath { get; }

    public LakonaProjectClient ClientKind { get; }

    public string? ClientVersion { get; }

    public string Client => FormatClient(ClientKind, ClientVersion);

    public string LakonaVersion => inspectedLakonaVersion ?? Text.NotDetected;

    public string StatusText => inspectionStatus == LakonaProjectStatus.Ready ? Text.ProjectReady : Text.ProjectNeedsAttention;

    public ObservableCollection<LocalApplicationInstallation> ServerEditors { get; } = [];

    public LocalApplicationInstallation? SelectedServerEditor
    {
        get => selectedServerEditor;
        set
        {
            if (selectedServerEditor == value)
            {
                return;
            }

            selectedServerEditor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpenServer));
            OnPropertyChanged(nameof(ServerOpenToolTip));
            if (ClientKind == LakonaProjectClient.Console)
            {
                ClientApplication = value;
            }
        }
    }

    public LocalApplicationInstallation? ClientApplication
    {
        get => clientApplication;
        private set
        {
            if (clientApplication == value)
            {
                return;
            }

            clientApplication = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanOpenClient));
            OnPropertyChanged(nameof(ClientOpenToolTip));
            OnPropertyChanged(nameof(ClientActionText));
        }
    }

    public string LastOpened
    {
        get
        {
            if (lastOpenedAtUtc is null)
            {
                return Text.NeverOpened;
            }

            var elapsed = timeProvider.GetUtcNow() - lastOpenedAtUtc.Value;
            if (elapsed < TimeSpan.FromMinutes(1))
            {
                return Text.JustNow;
            }

            if (elapsed < TimeSpan.FromHours(1))
            {
                return Text.MinutesAgo(Math.Max(1, (int)elapsed.TotalMinutes));
            }

            if (elapsed < TimeSpan.FromDays(1))
            {
                return Text.HoursAgo(Math.Max(1, (int)elapsed.TotalHours));
            }

            if (elapsed < TimeSpan.FromDays(7))
            {
                return Text.DaysAgo(Math.Max(1, (int)elapsed.TotalDays));
            }

            return lastOpenedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    internal DateTimeOffset? LastOpenedAtUtc => lastOpenedAtUtc;

    public bool CanOpenServer => SelectedServerEditor is not null;

    public bool CanOpenClient => ClientApplication is not null;

    public string OpenText => Text.Open;

    public string ServerActionText => Text.OpenServer;

    public string MoreActionsText => Text.MoreActions;

    public string OpenProjectFolderText => Text.OpenProjectFolder;

    public string RemoveFromListText => Text.RemoveFromList;

    public string ClientActionText => ClientKind switch
    {
        LakonaProjectClient.Unity => Text.ClientAction("Unity"),
        LakonaProjectClient.Godot => Text.ClientAction("Godot"),
        LakonaProjectClient.Tuanjie => Text.ClientAction(Text.Tuanjie),
        LakonaProjectClient.Console when ClientApplication is not null => Text.ClientAction(ClientApplication.DisplayName),
        _ => Text.OpenClientAction
    };

    public string ServerOpenToolTip => SelectedServerEditor is null
        ? Text.NoServerIde
        : Text.OpenServerWith(SelectedServerEditor.DisplayName);

    public string ClientOpenToolTip => ClientApplication is null
        ? ClientKind == LakonaProjectClient.Console
            ? Text.NoServerIde
            : Text.NoClientEditor(ClientName(ClientKind))
        : Text.OpenClientWith(ClientApplication.DisplayName);

    public static ProjectListItem FromInspection(
        LakonaProjectInspection inspection,
        IReadOnlyList<LocalApplicationInstallation> applications,
        HubLocalization? localization = null,
        string? preferredServerEditorPath = null,
        DateTimeOffset? lastOpenedAtUtc = null,
        TimeProvider? timeProvider = null)
    {
        var item = new ProjectListItem(
            inspection,
            localization ?? new HubLocalization(),
            preferredServerEditorPath,
            lastOpenedAtUtc,
            timeProvider ?? TimeProvider.System);
        item.RefreshApplications(applications);
        return item;
    }

    public void RefreshApplications(IReadOnlyList<LocalApplicationInstallation> applications)
    {
        var previousPath = SelectedServerEditor?.ExecutablePath ?? preferredServerEditorPath;
        preferredServerEditorPath = null;
        ServerEditors.Clear();
        foreach (var editor in InstalledApplicationCatalog.ServerEditors(applications))
        {
            ServerEditors.Add(editor);
        }

        SelectedServerEditor = ServerEditors.FirstOrDefault(editor =>
                                   string.Equals(editor.ExecutablePath, previousPath, StringComparison.OrdinalIgnoreCase)) ??
                               ServerEditors.FirstOrDefault();

        if (ClientKind == LakonaProjectClient.Console)
        {
            ClientApplication = SelectedServerEditor;
            return;
        }

        var clientApplicationKind = ClientKind switch
        {
            LakonaProjectClient.Unity => LocalApplicationKind.Unity,
            LakonaProjectClient.Tuanjie => LocalApplicationKind.Tuanjie,
            LakonaProjectClient.Godot => LocalApplicationKind.Godot,
            _ => (LocalApplicationKind?)null
        };
        var clientCandidates = clientApplicationKind is null
            ? []
            : applications.Where(application => application.Kind == clientApplicationKind).ToArray();
        ClientApplication = BestVersionMatch(clientCandidates, ClientVersion);
    }

    public void MarkOpened()
    {
        lastOpenedAtUtc = timeProvider.GetUtcNow();
        OnPropertyChanged(nameof(LastOpened));
    }

    public void RefreshLastOpened() => OnPropertyChanged(nameof(LastOpened));

    private static LocalApplicationInstallation? BestVersionMatch(
        IReadOnlyList<LocalApplicationInstallation> candidates,
        string? projectVersion)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(projectVersion))
        {
            var match = candidates.FirstOrDefault(application =>
                application.Version?.StartsWith(projectVersion, StringComparison.OrdinalIgnoreCase) == true ||
                application.ExecutablePath.Contains(projectVersion, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return candidates[0];
    }

    private string FormatClient(LakonaProjectClient client, string? version)
    {
        var name = ClientName(client);
        return string.IsNullOrWhiteSpace(version) ? name : $"{name} {version}";
    }

    private string ClientName(LakonaProjectClient client) => client switch
    {
        LakonaProjectClient.Unity => "Unity",
        LakonaProjectClient.Tuanjie => Text.Tuanjie,
        LakonaProjectClient.Godot => "Godot",
        LakonaProjectClient.Console => "Console",
        _ => Text.Unknown
    };

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HubLocalization.Text))
        {
            return;
        }

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Client));
        OnPropertyChanged(nameof(LakonaVersion));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LastOpened));
        OnPropertyChanged(nameof(ClientActionText));
        OnPropertyChanged(nameof(OpenText));
        OnPropertyChanged(nameof(ServerActionText));
        OnPropertyChanged(nameof(MoreActionsText));
        OnPropertyChanged(nameof(OpenProjectFolderText));
        OnPropertyChanged(nameof(RemoveFromListText));
        OnPropertyChanged(nameof(ServerOpenToolTip));
        OnPropertyChanged(nameof(ClientOpenToolTip));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
