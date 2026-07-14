namespace Lakona.Hub.Applications;

internal interface IApplicationProbeSource
{
    IEnumerable<LocalApplicationInstallation> FindApplications();
}

internal sealed class InstalledApplicationCatalog
{
    private static readonly IReadOnlyDictionary<LocalApplicationKind, int> KindOrder =
        new Dictionary<LocalApplicationKind, int>
        {
            [LocalApplicationKind.Rider] = 0,
            [LocalApplicationKind.VisualStudio] = 1,
            [LocalApplicationKind.VisualStudioCode] = 2,
            [LocalApplicationKind.Unity] = 3,
            [LocalApplicationKind.Godot] = 4
        };

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
            .OrderBy(application => KindOrder[application.Kind])
            .ThenByDescending(application => application.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<LocalApplicationInstallation> ServerEditors(
        IEnumerable<LocalApplicationInstallation> applications)
    {
        return applications
            .Where(application => application.Kind is
                LocalApplicationKind.Rider or
                LocalApplicationKind.VisualStudio or
                LocalApplicationKind.VisualStudioCode)
            .OrderBy(application => KindOrder[application.Kind])
            .ToArray();
    }
}
