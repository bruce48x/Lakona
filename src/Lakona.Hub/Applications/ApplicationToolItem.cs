using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lakona.Hub.Applications;

public sealed class ApplicationToolItem : INotifyPropertyChanged
{
    private readonly HubLocalization localization;
    private LocalApplicationInstallation? installation;
    private string? configuredPath;

    internal ApplicationToolItem(LocalApplicationKind kind, HubLocalization localization)
    {
        Kind = kind;
        this.localization = localization;
        localization.PropertyChanged += Localization_PropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalApplicationKind Kind { get; }

    public string DisplayName => Kind switch
    {
        LocalApplicationKind.Rider => "Rider",
        LocalApplicationKind.VisualStudio => "Visual Studio",
        LocalApplicationKind.VisualStudioCode => "VS Code",
        LocalApplicationKind.Unity => "Unity",
        LocalApplicationKind.Godot => "Godot",
        _ => Kind.ToString()
    };

    public string StatusText => installation is not null
        ? localization.Text.DetectedTool(installation.Version)
        : configuredPath is not null
            ? localization.Text.ConfiguredToolUnavailable
            : localization.Text.NotDetected;

    public string PathText => installation?.ExecutablePath ?? configuredPath ?? localization.Text.NoConfiguredPath;

    public string BrowseText => localization.Text.Browse;

    internal string? SuggestedPath => installation?.ExecutablePath ?? configuredPath;

    internal void Update(LocalApplicationInstallation? detectedInstallation, string? savedPath)
    {
        installation = detectedInstallation;
        configuredPath = savedPath;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(PathText));
    }

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HubLocalization.Text))
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(PathText));
            OnPropertyChanged(nameof(BrowseText));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
