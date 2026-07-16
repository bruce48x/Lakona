using System.Text.Json;

namespace Lakona.Hub;

internal sealed record HubUserSettings(
    HubLanguage Language,
    IReadOnlyList<string> ProjectPaths);

internal sealed record StoredHubUserSettings(
    int SchemaVersion,
    string Language,
    List<string> ProjectPaths);

internal sealed class HubUserSettingsStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string settingsFilePath;

    public HubUserSettingsStore(string? settingsFilePath = null)
    {
        this.settingsFilePath = settingsFilePath ?? DefaultSettingsFilePath();
    }

    public HubUserSettings Load(HubLanguage detectedLanguage)
    {
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return new HubUserSettings(detectedLanguage, []);
            }

            var settings = JsonSerializer.Deserialize(
                File.ReadAllText(settingsFilePath),
                HubJsonContext.Default.StoredHubUserSettings);
            if (settings is not { SchemaVersion: CurrentSchemaVersion, ProjectPaths: not null } ||
                !Enum.TryParse<HubLanguage>(settings.Language, out var language) ||
                !Enum.IsDefined(language))
            {
                return new HubUserSettings(detectedLanguage, []);
            }

            var projectPaths = settings.ProjectPaths
                .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new HubUserSettings(language, projectPaths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HubUserSettings(detectedLanguage, []);
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
            settings.ProjectPaths
                .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList());
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(settingsFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(stored, HubJsonContext.Default.StoredHubUserSettings));
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

    private static string DefaultSettingsFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".lakona",
                "hub");
        }
        else
        {
            root = Path.Combine(root, "Lakona", "Hub");
        }

        return Path.Combine(root, "user-settings.json");
    }
}
