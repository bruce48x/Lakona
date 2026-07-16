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
            .Where(application => File.Exists(application.ExecutablePath))
            .DistinctBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(application => LocalApplicationKinds.Order(application.Kind))
            .ThenByDescending(application => application.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
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
        return automaticallyDetected
            .Concat(manuallyConfigured)
            .OrderBy(application => LocalApplicationKinds.Order(application.Kind))
            .ThenBy(application => manuallyConfigured.Any(manual =>
                string.Equals(manual.ExecutablePath, application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
            .ThenByDescending(application => application.Version, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
