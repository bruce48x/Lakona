using System.Text.Json;

namespace Lakona.Hub.Applications;

internal sealed class ApplicationPathStore
{
    private readonly string settingsFilePath;

    public ApplicationPathStore(string? settingsFilePath = null)
    {
        this.settingsFilePath = settingsFilePath ?? DefaultSettingsFilePath();
    }

    public IReadOnlyDictionary<LocalApplicationKind, string> Load()
    {
        try
        {
            if (!File.Exists(settingsFilePath))
            {
                return new Dictionary<LocalApplicationKind, string>();
            }

            var serialized = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(settingsFilePath));
            if (serialized is null)
            {
                return new Dictionary<LocalApplicationKind, string>();
            }

            var result = new Dictionary<LocalApplicationKind, string>();
            foreach (var (kindName, path) in serialized)
            {
                if (Enum.TryParse<LocalApplicationKind>(kindName, out var kind) &&
                    !string.IsNullOrWhiteSpace(path))
                {
                    result[kind] = path;
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<LocalApplicationKind, string>();
        }
    }

    public void Save(IReadOnlyDictionary<LocalApplicationKind, string> paths)
    {
        var directory = Path.GetDirectoryName(settingsFilePath)
            ?? throw new InvalidOperationException("The application path settings directory is unavailable.");
        Directory.CreateDirectory(directory);

        var serialized = paths.ToDictionary(
            pair => pair.Key.ToString(),
            pair => pair.Value,
            StringComparer.Ordinal);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(settingsFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(serialized, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
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
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".lakona",
                "hub",
                "application-paths.json");
        }

        return Path.Combine(root, "Lakona", "Hub", "application-paths.json");
    }
}
