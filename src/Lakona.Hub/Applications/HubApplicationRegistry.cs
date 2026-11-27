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
        var key = ApplicationExecutable.InstallationKey(registration.Kind, registration.ExecutablePath);
        if (InstalledApplications.Any(application => SamePath(
                ApplicationExecutable.InstallationKey(application.Kind, application.ExecutablePath), key)) ||
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

        var key = ApplicationExecutable.InstallationKey(tool.Kind, path);
        var updated = manualApplications
            .Where(application => !SamePath(ApplicationExecutable.InstallationKey(application.Kind, application.ExecutablePath), key))
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
        var manuallyConfigured = new List<LocalApplicationInstallation>();
        for (var i = 0; i < manualApplications.Count; i++)
        {
            var registration = manualApplications[i];
            // Older Linux settings can contain Godot or Tuanjie Hub saved as a generic IDE.
            var recognized = OperatingSystem.IsLinux() && registration.Kind == LocalApplicationKind.Other
                ? SystemApplicationProbeSource.TryCreateManualInstallation(registration.ExecutablePath, out var installation)
                : SystemApplicationProbeSource.TryCreateInstallation(registration.Kind, registration.ExecutablePath, out installation);
            if (!recognized)
            {
                continue;
            }

            if (installation.Kind == registration.Kind)
            {
                installation = installation with { DisplayName = registration.DisplayName };
            }
            manualApplications[i] = new(installation.Kind, installation.DisplayName, installation.ExecutablePath);
            manuallyConfigured.Add(installation);
        }
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
        ApplicationExecutable.PathComparer.Equals(left, right);
}
