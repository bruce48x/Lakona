using System.Xml;
using System.Xml.Linq;

namespace Lakona.ProjectSystem;

public sealed class LakonaProjectInspector
{
    private const long MaxClientMetadataBytes = 64 * 1024;
    private const long MaxProjectFileCharacters = 1024 * 1024;

    private static readonly string[] RequiredPaths =
    [
        "Shared/Shared.csproj",
        "Server/Server.slnx",
        "Server/App/Server.App.csproj",
        "Server/Hotfix/Server.Hotfix.csproj"
    ];

    public LakonaProjectInspection Inspect(string? projectRoot)
    {
        if (!TryResolveRoot(projectRoot, out var rootPath))
        {
            return Result(
                projectRoot ?? string.Empty,
                string.Empty,
                LakonaProjectStatus.NotFound,
                diagnostics: [new("invalid-path", "Choose an existing project directory.")]);
        }

        var name = new DirectoryInfo(rootPath).Name;
        var existingRequiredPaths = RequiredPaths
            .Where(relativePath => File.Exists(Resolve(rootPath, relativePath)))
            .ToArray();

        if (existingRequiredPaths.Length == 0)
        {
            return Result(
                rootPath,
                name,
                LakonaProjectStatus.NotLakonaProject,
                diagnostics: [new("not-lakona-project", "The directory does not contain a Lakona project layout.")]);
        }

        var diagnostics = RequiredPaths
            .Except(existingRequiredPaths, StringComparer.OrdinalIgnoreCase)
            .Select(relativePath => new LakonaProjectDiagnostic(
                "missing-project-file",
                $"Required project file is missing: {relativePath}"))
            .ToList();

        var client = InspectClient(rootPath, diagnostics);
        var lakonaVersion = ReadLakonaVersion(rootPath, diagnostics);
        var status = diagnostics.Any(diagnostic => diagnostic.Code == "missing-project-file") ||
                     diagnostics.Any(diagnostic => diagnostic.Code == "server-project-unreadable") ||
                     client.Client == LakonaProjectClient.Unknown
            ? LakonaProjectStatus.Incomplete
            : LakonaProjectStatus.Ready;

        return Result(
            rootPath,
            name,
            status,
            client.Client,
            client.Version,
            lakonaVersion,
            diagnostics);
    }

    private static (LakonaProjectClient Client, string? Version) InspectClient(
        string rootPath,
        ICollection<LakonaProjectDiagnostic> diagnostics)
    {
        var unityVersionPath = Resolve(rootPath, "Client/ProjectSettings/ProjectVersion.txt");
        if (File.Exists(unityVersionPath))
        {
            try
            {
                var content = ReadClientMetadata(unityVersionPath);
                var tuanjieVersion = ReadLineValue(content, "m_TuanjieEditorVersion:");
                return tuanjieVersion is not null
                    ? (LakonaProjectClient.Tuanjie, tuanjieVersion)
                    : (LakonaProjectClient.Unity, ReadLineValue(content, "m_EditorVersion:"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                diagnostics.Add(new("client-version-unreadable", $"Unable to read Client/ProjectSettings/ProjectVersion.txt: {ex.Message}"));
                return (LakonaProjectClient.Unknown, null);
            }
        }

        var godotProjectPath = Resolve(rootPath, "Client/project.godot");
        if (File.Exists(godotProjectPath))
        {
            try
            {
                var content = ReadClientMetadata(godotProjectPath);
                return (LakonaProjectClient.Godot, ReadGodotVersion(content));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                diagnostics.Add(new("client-version-unreadable", $"Unable to read Client/project.godot: {ex.Message}"));
                return (LakonaProjectClient.Unknown, null);
            }
        }

        if (File.Exists(Resolve(rootPath, "Client/Client.csproj")))
        {
            return (LakonaProjectClient.Console, null);
        }

        diagnostics.Add(new("missing-client", "No supported client project was found under Client/."));
        return (LakonaProjectClient.Unknown, null);
    }

    private static string? ReadLakonaVersion(
        string rootPath,
        ICollection<LakonaProjectDiagnostic> diagnostics)
    {
        var serverProjectPath = Resolve(rootPath, "Server/App/Server.App.csproj");
        if (!File.Exists(serverProjectPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(serverProjectPath);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxProjectFileCharacters
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            var packageReference = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "PackageReference" &&
                    string.Equals(
                        (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update"),
                        "Lakona.Game.Server",
                        StringComparison.OrdinalIgnoreCase));

            var version = (string?)packageReference?.Attribute("Version") ??
                          packageReference?.Elements().FirstOrDefault(element => element.Name.LocalName == "Version")?.Value;
            version = version?.Trim();
            return string.IsNullOrWhiteSpace(version) || version.Contains("$(", StringComparison.Ordinal)
                ? null
                : version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            diagnostics.Add(new("server-project-unreadable", $"Unable to inspect Server/App/Server.App.csproj: {ex.Message}"));
            return null;
        }
    }

    private static string? ReadLineValue(string content, string key)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(key, StringComparison.Ordinal))
            {
                var value = trimmed[key.Length..].Trim();
                return value.Length == 0 ? null : value;
            }
        }

        return null;
    }

    private static string ReadClientMetadata(string path)
    {
        if (new FileInfo(path).Length > MaxClientMetadataBytes)
        {
            throw new InvalidDataException($"Metadata file exceeds the {MaxClientMetadataBytes / 1024} KB inspection limit.");
        }

        return File.ReadAllText(path);
    }

    private static string? ReadGodotVersion(string content)
    {
        const string marker = "config/features=PackedStringArray(\"";
        var start = content.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = content.IndexOf('"', start);
        return end <= start ? null : content[start..end];
    }

    private static bool TryResolveRoot(string? projectRoot, out string rootPath)
    {
        rootPath = string.Empty;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return false;
        }

        try
        {
            rootPath = Path.GetFullPath(projectRoot);
            return Directory.Exists(rootPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string Resolve(string rootPath, string relativePath)
    {
        return Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static LakonaProjectInspection Result(
        string rootPath,
        string name,
        LakonaProjectStatus status,
        LakonaProjectClient client = LakonaProjectClient.Unknown,
        string? clientVersion = null,
        string? lakonaVersion = null,
        IReadOnlyList<LakonaProjectDiagnostic>? diagnostics = null)
    {
        return new LakonaProjectInspection(
            rootPath,
            name,
            status,
            client,
            clientVersion,
            lakonaVersion,
            diagnostics ?? []);
    }
}
