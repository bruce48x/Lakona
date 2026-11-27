namespace Lakona.Hub.Applications;

internal interface IApplicationProbeSource
{
    IEnumerable<LocalApplicationInstallation> FindApplications();
}

internal sealed class InstalledApplicationCatalog
{
    private readonly IApplicationProbeSource source;

    public InstalledApplicationCatalog(IApplicationProbeSource? source = null)
    {
        this.source = source ?? new SystemApplicationProbeSource();
    }

    public IReadOnlyList<LocalApplicationInstallation> Detect()
    {
        return source.FindApplications()
            .Where(application => Path.IsPathFullyQualified(application.ExecutablePath))
            .Where(application => ApplicationExecutable.IsRunnable(application.ExecutablePath))
            .DistinctBy(InstallationKey, ApplicationExecutable.PathComparer)
            .OrderBy(application => LocalApplicationKinds.Order(application.Kind))
            .ThenByDescending(application => ParseSemanticVersion(application.Version))
            .ThenByDescending(application => application.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.ExecutablePath, ApplicationExecutable.PathComparer)
            .ToArray();
    }

    public static IReadOnlyList<LocalApplicationInstallation> ServerEditors(
        IEnumerable<LocalApplicationInstallation> applications)
    {
        return applications
            .Where(application => LocalApplicationKinds.IsServerEditor(application.Kind))
            .OrderBy(application => LocalApplicationKinds.Order(application.Kind))
            .ToArray();
    }

    public static IReadOnlyList<LocalApplicationInstallation> MergePreferred(
        IReadOnlyList<LocalApplicationInstallation> automaticallyDetected,
        IReadOnlyList<LocalApplicationInstallation> manuallyConfigured)
    {
        var manualPaths = manuallyConfigured.Select(app => app.ExecutablePath).ToHashSet(ApplicationExecutable.PathComparer);
        return manuallyConfigured
            .Concat(automaticallyDetected)
            .DistinctBy(InstallationKey, ApplicationExecutable.PathComparer)
            .OrderBy(application => LocalApplicationKinds.Order(application.Kind))
            .ThenByDescending(application => manualPaths.Contains(application.ExecutablePath))
            .ThenByDescending(application => ParseSemanticVersion(application.Version))
            .ThenByDescending(application => application.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string InstallationKey(LocalApplicationInstallation application) =>
        ApplicationExecutable.InstallationKey(application.Kind, application.ExecutablePath);

    private static Version? ParseSemanticVersion(string? value) =>
        Version.TryParse(value, out var version) ? version : null;
}
