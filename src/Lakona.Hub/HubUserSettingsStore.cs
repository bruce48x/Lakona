using System.Text.Json;

namespace Lakona.Hub;

internal sealed record HubProjectSettings(string Path, string? SelectedServerEditorPath, DateTimeOffset? LastOpenedAtUtc);

internal sealed record HubDetectedApplicationSettings(string Kind, string DisplayName, string ExecutablePath, string? Version);

internal sealed record HubCreationDraft(
    string ProjectName,
    string OutputDirectory,
    string ClientId,
    string? ClientVersionId,
    string TransportId,
    string SerializerId,
    string NuGetSourceId);

internal sealed record HubWindowSettings(int X, int Y, double Width, double Height, string State);

internal sealed record HubUpdateCheckSettings(
    DateTimeOffset CheckedAtUtc,
    string? Version,
    string? Platform,
    string? Tag,
    string? AssetName,
    string? Sha256,
    long? Size);

internal sealed record HubUserSettings(
    HubLanguage Language,
    IReadOnlyList<HubProjectSettings> Projects,
    IReadOnlyList<HubDetectedApplicationSettings> DetectedApplications,
    HubCreationDraft? CreationDraft,
    string CurrentPage,
    HubWindowSettings? Window,
    HubUpdateCheckSettings? UpdateCheck);

internal sealed record StoredHubUserSettings(
    int SchemaVersion,
    string Language,
    List<string>? ProjectPaths,
    List<HubProjectSettings>? Projects,
    List<HubDetectedApplicationSettings>? DetectedApplications,
    HubCreationDraft? CreationDraft,
    string? CurrentPage,
    HubWindowSettings? Window,
    HubUpdateCheckSettings? UpdateCheck);

internal sealed class HubUserSettingsStore
{
    private const int CurrentSchemaVersion = 2;
    private readonly string settingsFilePath;

    public HubUserSettingsStore(string? settingsFilePath = null) =>
        this.settingsFilePath = settingsFilePath ?? DefaultSettingsFilePath();

    public HubUserSettings Load(HubLanguage detectedLanguage)
    {
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return Defaults(detectedLanguage);
            }

            var settings = JsonSerializer.Deserialize(
                File.ReadAllText(settingsFilePath),
                HubJsonContext.Default.StoredHubUserSettings);
            if (settings is null ||
                !Enum.TryParse<HubLanguage>(settings.Language, out var language) ||
                !Enum.IsDefined(language))
            {
                return Defaults(detectedLanguage);
            }

            var projects = settings.SchemaVersion switch
            {
                1 => ParseLegacyProjects(settings.ProjectPaths),
                CurrentSchemaVersion => ParseProjects(settings.Projects),
                _ => []
            };
            if (settings.SchemaVersion is not (1 or CurrentSchemaVersion))
            {
                return Defaults(detectedLanguage);
            }

            return new HubUserSettings(
                language,
                projects,
                ParseApplications(settings.DetectedApplications),
                settings.CreationDraft,
                settings.CurrentPage is "Settings" ? "Settings" : "Projects",
                ParseWindow(settings.Window),
                settings.UpdateCheck);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Defaults(detectedLanguage);
        }
    }

    public void Save(HubUserSettings settings)
    {
        var directory = Path.GetDirectoryName(settingsFilePath)
            ?? throw new InvalidOperationException("The Hub user settings directory is unavailable.");
        Directory.CreateDirectory(directory);

        var stored = new StoredHubUserSettings(
            CurrentSchemaVersion,
            settings.Language.ToString(),
            null,
            ParseProjects(settings.Projects).ToList(),
            ParseApplications(settings.DetectedApplications).ToList(),
            settings.CreationDraft,
            settings.CurrentPage is "Settings" ? "Settings" : "Projects",
            ParseWindow(settings.Window),
            settings.UpdateCheck);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(settingsFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(stored, HubJsonContext.Default.StoredHubUserSettings));
            File.Move(temporaryPath, settingsFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static HubUserSettings Defaults(HubLanguage language) =>
        new(language, [], [], null, "Projects", null, null);

    private static IReadOnlyList<HubProjectSettings> ParseLegacyProjects(IEnumerable<string>? paths) =>
        ParseProjects(paths?.Select(path => new HubProjectSettings(path, null, null)));

    private static IReadOnlyList<HubProjectSettings> ParseProjects(IEnumerable<HubProjectSettings>? projects) =>
        (projects ?? [])
        .Where(project => !string.IsNullOrWhiteSpace(project.Path) && Path.IsPathFullyQualified(project.Path))
        .DistinctBy(project => project.Path, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static IReadOnlyList<HubDetectedApplicationSettings> ParseApplications(
        IEnumerable<HubDetectedApplicationSettings>? applications) =>
        (applications ?? [])
        .Where(application =>
            !string.IsNullOrWhiteSpace(application.DisplayName) &&
            Path.IsPathFullyQualified(application.ExecutablePath))
        .DistinctBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static HubWindowSettings? ParseWindow(HubWindowSettings? window) =>
        window is { Width: >= 1000 and <= 10000, Height: >= 800 and <= 10000 }
            ? window with { State = window.State == "Maximized" ? "Maximized" : "Normal" }
            : null;

    private static string DefaultSettingsFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        root = string.IsNullOrWhiteSpace(root)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lakona", "hub")
            : Path.Combine(root, "Lakona", "Hub");
        return Path.Combine(root, "user-settings.json");
    }
}
