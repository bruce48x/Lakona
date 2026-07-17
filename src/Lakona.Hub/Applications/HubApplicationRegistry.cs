using System.Collections.ObjectModel;

namespace Lakona.Hub.Applications;

internal sealed class HubApplicationRegistry : IDisposable
{
    private readonly InstalledApplicationCatalog catalog;
    private readonly ManualApplicationStore store;
    private readonly HubLocalization localization;
    private readonly List<ManualApplicationRegistration> manualApplications;

    public HubApplicationRegistry(
        InstalledApplicationCatalog catalog,
        ManualApplicationStore store,
        HubLocalization localization,
        IReadOnlyList<LocalApplicationInstallation> cachedApplications)
    {
        this.catalog = catalog;
        this.store = store;
        this.localization = localization;
        AutomaticApplications = cachedApplications;
        manualApplications = store.Load().ToList();
        Rebuild();
    }

    public ObservableCollection<ApplicationToolItem> Tools { get; } = [];

    public IReadOnlyList<LocalApplicationInstallation> AutomaticApplications { get; private set; }

    public IReadOnlyList<LocalApplicationInstallation> InstalledApplications { get; private set; } = [];

    public async Task DetectAsync(CancellationToken cancellationToken)
    {
        AutomaticApplications = await Task.Run(catalog.Detect, cancellationToken);
        Rebuild();
    }

    public bool TryAddManual(ManualApplicationRegistration registration)
    {
        if (InstalledApplications.Any(application => SamePath(application.ExecutablePath, registration.ExecutablePath)) ||
            manualApplications.Any(application => SamePath(application.ExecutablePath, registration.ExecutablePath)))
        {
            return false;
        }

        var updated = manualApplications.Append(registration).ToArray();
        store.Save(updated);
        manualApplications.Clear();
        manualApplications.AddRange(updated);
        Rebuild();
        return true;
    }

    public bool RemoveManual(ApplicationToolItem tool)
    {
        if (tool.ManualPath is not { } path)
        {
            return false;
        }

        var updated = manualApplications
            .Where(application => !SamePath(application.ExecutablePath, path))
            .ToArray();
        store.Save(updated);
        manualApplications.Clear();
        manualApplications.AddRange(updated);
        Rebuild();
        return true;
    }

    public void Dispose()
    {
        foreach (var tool in Tools)
        {
            tool.Dispose();
        }
        Tools.Clear();
    }

    private void Rebuild()
    {
        var manuallyConfigured = manualApplications
            .Select(registration => SystemApplicationProbeSource.TryCreateInstallation(
                    registration.Kind,
                    registration.ExecutablePath,
                    out var installation)
                ? installation with { DisplayName = registration.DisplayName }
                : null)
            .OfType<LocalApplicationInstallation>()
            .ToArray();
        InstalledApplications = InstalledApplicationCatalog.MergePreferred(
            AutomaticApplications,
            manuallyConfigured);

        foreach (var tool in Tools)
        {
            tool.Dispose();
        }
        Tools.Clear();
        foreach (var tool in ApplicationToolList.Build(InstalledApplications, manualApplications, localization))
        {
            Tools.Add(tool);
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
