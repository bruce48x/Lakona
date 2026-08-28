using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Lakona.Hub.Applications;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed class ProjectListItem : INotifyPropertyChanged, IDisposable
{
    private readonly HubLocalization localization;
    private readonly string? inspectedName;
    private readonly string? inspectedLakonaVersion;
    private readonly LakonaProjectStatus inspectionStatus;
    private readonly TimeProvider timeProvider;
    private readonly DateTimeOffset? addedAtUtc;
    private LocalApplicationInstallation? serverEditor;
    private LocalApplicationInstallation? clientApplication;
    private DateTimeOffset? lastOpenedAtUtc;

    private ProjectListItem(
        LakonaProjectInspection inspection,
        HubLocalization localization,
        DateTimeOffset? lastOpenedAtUtc,
        DateTimeOffset? addedAtUtc,
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
        BuildTag = inspection.BuildTag ?? "";
        this.lastOpenedAtUtc = lastOpenedAtUtc;
        this.addedAtUtc = addedAtUtc;
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

    public string BuildTag { get; }

    public string Client => FormatClient(ClientKind, ClientVersion);

    public string LakonaVersion => inspectedLakonaVersion ?? Text.NotDetected;

    public string StatusText => inspectionStatus == LakonaProjectStatus.Ready ? Text.ProjectReady : Text.ProjectNeedsAttention;

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

    internal DateTimeOffset? AddedAtUtc => addedAtUtc;

    internal DateTimeOffset? RecentActivityAtUtc => lastOpenedAtUtc ?? addedAtUtc;

    public bool CanOpenServer => serverEditor is not null;

    public bool CanOpenClient => ClientApplication is not null;

    public string OpenText => Text.Open;

    public string PackageText => Text.Package;

    public bool CanPackage => inspectionStatus == LakonaProjectStatus.Ready;

    public string MoreActionsText => Text.MoreActions;

    public string OpenProjectFolderText => Text.OpenProjectFolder;

    public string RemoveFromListText => Text.RemoveFromList;

    public string ServerOpenToolTip => serverEditor is null
        ? Text.NoServerIde
        : Text.OpenServerWith(serverEditor.DisplayName);

    public string ClientOpenToolTip => ClientApplication is null
        ? ClientKind == LakonaProjectClient.Console
            ? Text.NoServerIde
            : Text.NoClientEditor(ClientName(ClientKind))
        : Text.OpenClientWith(ClientApplication.DisplayName);

    public static ProjectListItem FromInspection(
        LakonaProjectInspection inspection,
        IReadOnlyList<LocalApplicationInstallation> applications,
        HubLocalization? localization = null,
        LocalApplicationInstallation? serverEditor = null,
        DateTimeOffset? lastOpenedAtUtc = null,
        DateTimeOffset? addedAtUtc = null,
        TimeProvider? timeProvider = null)
    {
        var item = new ProjectListItem(
            inspection,
            localization ?? new HubLocalization(),
            lastOpenedAtUtc,
            addedAtUtc,
            timeProvider ?? TimeProvider.System);
        item.RefreshApplications(applications, serverEditor);
        return item;
    }

    public void RefreshApplications(
        IReadOnlyList<LocalApplicationInstallation> applications,
        LocalApplicationInstallation? selectedServerEditor)
    {
        UpdateServerEditor(selectedServerEditor);

        if (ClientKind == LakonaProjectClient.Console)
        {
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

    public void UpdateServerEditor(LocalApplicationInstallation? selectedServerEditor)
    {
        if (ReferenceEquals(serverEditor, selectedServerEditor))
        {
            return;
        }

        serverEditor = selectedServerEditor;
        OnPropertyChanged(nameof(CanOpenServer));
        OnPropertyChanged(nameof(ServerOpenToolTip));
        if (ClientKind == LakonaProjectClient.Console)
        {
            ClientApplication = selectedServerEditor;
        }
    }

    public void MarkOpened()
    {
        lastOpenedAtUtc = timeProvider.GetUtcNow();
        OnPropertyChanged(nameof(LastOpened));
    }

    public void RefreshLastOpened() => OnPropertyChanged(nameof(LastOpened));

    public void Dispose() => localization.PropertyChanged -= Localization_PropertyChanged;

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
        OnPropertyChanged(nameof(OpenText));
        OnPropertyChanged(nameof(PackageText));
        OnPropertyChanged(nameof(MoreActionsText));
        OnPropertyChanged(nameof(OpenProjectFolderText));
        OnPropertyChanged(nameof(RemoveFromListText));
        OnPropertyChanged(nameof(ServerOpenToolTip));
        OnPropertyChanged(nameof(ClientOpenToolTip));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
