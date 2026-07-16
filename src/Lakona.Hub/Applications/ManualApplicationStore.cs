using System.Text.Json;

namespace Lakona.Hub.Applications;

internal sealed record StoredManualApplication(
    string Kind,
    string DisplayName,
    string ExecutablePath);

internal sealed record StoredManualApplicationSettings(
    int SchemaVersion,
    List<StoredManualApplication> Applications);

internal sealed class ManualApplicationStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string settingsFilePath;

    public ManualApplicationStore(string? settingsFilePath = null)
    {
        this.settingsFilePath = settingsFilePath ?? DefaultSettingsFilePath();
    }

    public IReadOnlyList<ManualApplicationRegistration> Load()
    {
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return [];
            }

            var json = File.ReadAllText(settingsFilePath);
            try
            {
                var settings = JsonSerializer.Deserialize(
                    json,
                    HubJsonContext.Default.StoredManualApplicationSettings);
                return settings is { SchemaVersion: CurrentSchemaVersion, Applications: not null }
                    ? Parse(settings.Applications)
                    : LoadLegacyPaths(json);
            }
            catch (JsonException)
            {
                return LoadLegacyPaths(json);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public void Save(IReadOnlyList<ManualApplicationRegistration> applications)
    {
        var directory = Path.GetDirectoryName(settingsFilePath)
            ?? throw new InvalidOperationException("The application settings directory is unavailable.");
        Directory.CreateDirectory(directory);

        var stored = applications
            .Where(application =>
                !string.IsNullOrWhiteSpace(application.DisplayName) &&
                Path.IsPathFullyQualified(application.ExecutablePath))
            .DistinctBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(application => new StoredManualApplication(
                application.Kind.ToString(),
                application.DisplayName.Trim(),
                application.ExecutablePath))
            .ToList();
        var settings = new StoredManualApplicationSettings(CurrentSchemaVersion, stored);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(settingsFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, HubJsonContext.Default.StoredManualApplicationSettings));
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

    private static IReadOnlyList<ManualApplicationRegistration> Parse(
        IEnumerable<StoredManualApplication> applications)
    {
        return applications
            .Where(application =>
                TryParseKind(application.Kind, out _) &&
                !string.IsNullOrWhiteSpace(application.DisplayName) &&
                !string.IsNullOrWhiteSpace(application.ExecutablePath))
            .Select(application => new ManualApplicationRegistration(
                ParseKind(application.Kind),
                application.DisplayName.Trim(),
                application.ExecutablePath))
            .DistinctBy(application => application.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<ManualApplicationRegistration> LoadLegacyPaths(string json)
    {
        var paths = JsonSerializer.Deserialize(json, HubJsonContext.Default.ApplicationPaths);
        if (paths is null)
        {
            return [];
        }

        return paths
            .Where(pair =>
                TryParseKind(pair.Key, out _) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair =>
            {
                var kind = ParseKind(pair.Key);
                return new ManualApplicationRegistration(
                    kind,
                    LocalApplicationKinds.DisplayName(kind),
                    pair.Value);
            })
            .ToArray();
    }

    private static bool TryParseKind(string value, out LocalApplicationKind kind) =>
        Enum.TryParse(value, out kind) && Enum.IsDefined(kind);

    private static LocalApplicationKind ParseKind(string value) =>
        Enum.Parse<LocalApplicationKind>(value);

    private static string DefaultSettingsFilePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".lakona",
                "hub",
                "application-paths.json");
        }

        return Path.Combine(root, "Lakona", "Hub", "application-paths.json");
    }
}
