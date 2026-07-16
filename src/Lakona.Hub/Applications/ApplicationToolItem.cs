using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Lakona.Hub.Applications;

public sealed class ApplicationToolItem : INotifyPropertyChanged
{
    private readonly HubLocalization localization;
    private readonly string displayName;
    private LocalApplicationInstallation? installation;
    private string? configuredPath;

    internal ApplicationToolItem(LocalApplicationKind kind, HubLocalization localization)
    {
        Kind = kind;
        this.localization = localization;
        displayName = LocalApplicationKinds.DisplayName(kind);
        localization.PropertyChanged += Localization_PropertyChanged;
    }

    internal ApplicationToolItem(
        LocalApplicationInstallation installation,
        HubLocalization localization,
        bool isManual)
        : this(installation.Kind, localization)
    {
        this.installation = installation;
        configuredPath = isManual ? installation.ExecutablePath : null;
        displayName = installation.DisplayName;
        IsManual = isManual;
    }

    internal ApplicationToolItem(
        ManualApplicationRegistration registration,
        HubLocalization localization)
        : this(registration.Kind, localization)
    {
        displayName = registration.DisplayName;
        configuredPath = registration.ExecutablePath;
        IsManual = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LocalApplicationKind Kind { get; }

    public string DisplayName => displayName;

    public bool IsManual { get; }

    public string StatusText => installation is not null
        ? IsManual
            ? localization.Text.ManuallyAddedTool(installation.Version)
            : localization.Text.DetectedTool(installation.Version)
        : configuredPath is not null
            ? localization.Text.ConfiguredToolUnavailable
            : localization.Text.NotDetected;

    public string PathText => installation?.ExecutablePath ?? configuredPath ?? localization.Text.NoConfiguredPath;

    public string ActionText => IsManual ? localization.Text.Remove : localization.Text.Browse;

    internal string? SuggestedPath => installation?.ExecutablePath ?? configuredPath;

    internal string? ManualPath => IsManual ? configuredPath : null;

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
            OnPropertyChanged(nameof(ActionText));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal static class ApplicationToolList
{
    public static IReadOnlyList<ApplicationToolItem> Build(
        IReadOnlyList<LocalApplicationInstallation> installedApplications,
        IReadOnlyList<ManualApplicationRegistration> manualApplications,
        HubLocalization localization)
    {
        var manualPaths = manualApplications
            .Select(application => application.ExecutablePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<ApplicationToolItem>();
        foreach (var kind in LocalApplicationKinds.AutomaticallyDetectedKinds.Append(LocalApplicationKind.Other))
        {
            var installations = installedApplications
                .Where(application => application.Kind == kind)
                .ToArray();
            var registrations = manualApplications
                .Where(application => application.Kind == kind)
                .ToArray();
            if (LocalApplicationKinds.DefaultVisibleKinds.Contains(kind) &&
                installations.Length == 0 &&
                registrations.Length == 0)
            {
                result.Add(new ApplicationToolItem(kind, localization));
            }

            result.AddRange(installations.Select(installation => new ApplicationToolItem(
                installation,
                localization,
                manualPaths.Contains(installation.ExecutablePath))));
            result.AddRange(registrations
                .Where(registration => installations.All(installation => !string.Equals(
                    installation.ExecutablePath,
                    registration.ExecutablePath,
                    StringComparison.OrdinalIgnoreCase)))
                .Select(registration => new ApplicationToolItem(registration, localization)));
        }

        return result;
    }
}
