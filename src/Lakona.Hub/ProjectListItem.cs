using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lakona.Hub.Applications;
using Lakona.ProjectSystem;

namespace Lakona.Hub;

public sealed class ProjectListItem : INotifyPropertyChanged
{
    private LocalApplicationInstallation? selectedServerEditor;
    private LocalApplicationInstallation? clientApplication;
    private string lastOpened = "刚刚";

    private ProjectListItem(LakonaProjectInspection inspection)
    {
        Name = string.IsNullOrWhiteSpace(inspection.Name) ? "未命名项目" : inspection.Name;
        Path = inspection.RootPath;
        ClientKind = inspection.Client;
        ClientVersion = inspection.ClientVersion;
        Client = FormatClient(inspection.Client, inspection.ClientVersion);
        LakonaVersion = inspection.LakonaVersion ?? "未检测到";
        StatusText = inspection.Status == LakonaProjectStatus.Ready ? "项目结构完整" : "项目结构需要检查";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string Path { get; }

    public LakonaProjectClient ClientKind { get; }

    public string? ClientVersion { get; }

    public string Client { get; }

    public string LakonaVersion { get; }

    public string StatusText { get; }

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
        get => lastOpened;
        private set
        {
            if (lastOpened == value)
            {
                return;
            }

            lastOpened = value;
            OnPropertyChanged();
        }
    }

    public bool CanOpenServer => SelectedServerEditor is not null;

    public bool CanOpenClient => ClientApplication is not null;

    public string ClientActionText => ClientKind switch
    {
        LakonaProjectClient.Unity => "Unity 打开",
        LakonaProjectClient.Godot => "Godot 打开",
        LakonaProjectClient.Tuanjie => "团结引擎打开",
        LakonaProjectClient.Console when ClientApplication is not null => $"{ClientApplication.DisplayName} 打开",
        _ => "打开客户端"
    };

    public string ServerOpenToolTip => SelectedServerEditor is null
        ? "未检测到 Rider、Visual Studio 或 VS Code"
        : $"使用 {SelectedServerEditor.DisplayName} 打开服务端";

    public string ClientOpenToolTip => ClientApplication is null
        ? ClientKind == LakonaProjectClient.Console
            ? "未检测到 Rider、Visual Studio 或 VS Code"
            : $"未检测到可用于 {ClientActionText.Replace(" 打开", string.Empty, StringComparison.Ordinal)} 的编辑器"
        : $"使用 {ClientApplication.DisplayName} 打开客户端";

    public static ProjectListItem FromInspection(
        LakonaProjectInspection inspection,
        IReadOnlyList<LocalApplicationInstallation> applications)
    {
        var item = new ProjectListItem(inspection);
        item.RefreshApplications(applications);
        return item;
    }

    public void RefreshApplications(IReadOnlyList<LocalApplicationInstallation> applications)
    {
        var previousPath = SelectedServerEditor?.ExecutablePath;
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
            LakonaProjectClient.Godot => LocalApplicationKind.Godot,
            _ => (LocalApplicationKind?)null
        };
        var clientCandidates = clientApplicationKind is null
            ? []
            : applications.Where(application => application.Kind == clientApplicationKind).ToArray();
        ClientApplication = BestVersionMatch(clientCandidates, ClientVersion);
    }

    public void MarkOpened() => LastOpened = "刚刚";

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

    private static string FormatClient(LakonaProjectClient client, string? version)
    {
        var name = client switch
        {
            LakonaProjectClient.Unity => "Unity",
            LakonaProjectClient.Tuanjie => "团结引擎",
            LakonaProjectClient.Godot => "Godot",
            LakonaProjectClient.Console => "Console",
            _ => "未识别"
        };
        return string.IsNullOrWhiteSpace(version) ? name : $"{name} {version}";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
