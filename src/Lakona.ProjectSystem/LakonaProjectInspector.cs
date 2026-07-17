using System.Xml;
using System.Xml.Linq;

namespace Lakona.ProjectSystem;

public sealed class LakonaProjectInspector
{
    private const long MaxClientMetadataBytes = 64 * 1024;
    private const long MaxProjectFileCharacters = 1024 * 1024;

    private static readonly string[] ServerMarkers =
    [
        "Server.slnx",
        "App/Server.App.csproj",
        "Hotfix/Server.Hotfix.csproj"
    ];

    private static readonly string[] ClientMarkers =
    [
        "ProjectSettings/ProjectVersion.txt",
        "project.godot",
        "Client.csproj"
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
        var layout = ResolveLayout(rootPath);
        var requiredPaths = RequiredPaths(rootPath, layout);
        var existingRequiredPaths = requiredPaths
            .Where(path => File.Exists(path.FullPath))
            .ToArray();

        if (existingRequiredPaths.Length == 0)
        {
            return Result(
                rootPath,
                name,
                LakonaProjectStatus.NotLakonaProject,
                diagnostics: [new("not-lakona-project", "The directory does not contain a Lakona project layout.")]);
        }

        var diagnostics = requiredPaths
            .Except(existingRequiredPaths)
            .Select(path => new LakonaProjectDiagnostic(
                "missing-project-file",
                $"Required project file is missing: {path.RelativePath}"))
            .ToList();

        var client = InspectClient(layout.ClientDirectory, diagnostics);
        var lakonaVersion = ReadLakonaVersion(requiredPaths[2], diagnostics);
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
            diagnostics,
            layout.ServerDirectory,
            layout.ClientDirectory);
    }

    private static (LakonaProjectClient Client, string? Version) InspectClient(
        string? clientDirectory,
        ICollection<LakonaProjectDiagnostic> diagnostics)
    {
        if (clientDirectory is null)
        {
            diagnostics.Add(new("missing-client", "No supported client project was found in a top-level directory."));
            return (LakonaProjectClient.Unknown, null);
        }

        var unityVersionPath = Path.Combine(clientDirectory, "ProjectSettings", "ProjectVersion.txt");
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
                diagnostics.Add(new("client-version-unreadable", $"Unable to read {unityVersionPath}: {ex.Message}"));
                return (LakonaProjectClient.Unknown, null);
            }
        }

        var godotProjectPath = Path.Combine(clientDirectory, "project.godot");
        if (File.Exists(godotProjectPath))
        {
            try
            {
                var content = ReadClientMetadata(godotProjectPath);
                return (LakonaProjectClient.Godot, ReadGodotVersion(content));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                diagnostics.Add(new("client-version-unreadable", $"Unable to read {godotProjectPath}: {ex.Message}"));
                return (LakonaProjectClient.Unknown, null);
            }
        }

        if (File.Exists(Path.Combine(clientDirectory, "Client.csproj")))
        {
            return (LakonaProjectClient.Console, null);
        }

        diagnostics.Add(new("missing-client", $"No supported client project was found in {clientDirectory}."));
        return (LakonaProjectClient.Unknown, null);
    }

    private static string? ReadLakonaVersion(
        LayoutPath serverProject,
        ICollection<LakonaProjectDiagnostic> diagnostics)
    {
        if (!File.Exists(serverProject.FullPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(serverProject.FullPath);
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
            diagnostics.Add(new("server-project-unreadable", $"Unable to inspect {serverProject.RelativePath}: {ex.Message}"));
            return null;
        }
    }

    private static ProjectLayout ResolveLayout(string rootPath) => new(
        FindTopLevelDirectory(rootPath, "Shared", directory => File.Exists(Path.Combine(directory, "Shared.csproj"))),
        FindTopLevelDirectory(rootPath, "Server", directory => ServerMarkers.Any(marker => File.Exists(Resolve(directory, marker)))),
        FindTopLevelDirectory(rootPath, "Client", directory => ClientMarkers.Any(marker => File.Exists(Resolve(directory, marker)))));

    private static LayoutPath[] RequiredPaths(string rootPath, ProjectLayout layout) =>
    [
        LayoutPath.Create(rootPath, layout.SharedDirectory, "Shared/Shared.csproj", "Shared.csproj"),
        LayoutPath.Create(rootPath, layout.ServerDirectory, "Server/Server.slnx", "Server.slnx"),
        LayoutPath.Create(rootPath, layout.ServerDirectory, "Server/App/Server.App.csproj", "App/Server.App.csproj"),
        LayoutPath.Create(rootPath, layout.ServerDirectory, "Server/Hotfix/Server.Hotfix.csproj", "Hotfix/Server.Hotfix.csproj")
    ];

    private static string? FindTopLevelDirectory(
        string rootPath,
        string preferredName,
        Func<string, bool> matches)
    {
        var preferredDirectory = Path.Combine(rootPath, preferredName);
        if (Directory.Exists(preferredDirectory) && matches(preferredDirectory))
        {
            return preferredDirectory;
        }

        try
        {
            return Directory.EnumerateDirectories(rootPath)
                .Where(directory => !string.Equals(directory, preferredDirectory, StringComparison.OrdinalIgnoreCase))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(matches);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
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
        IReadOnlyList<LakonaProjectDiagnostic>? diagnostics = null,
        string? serverPath = null,
        string? clientPath = null)
    {
        return new LakonaProjectInspection(
            rootPath,
            name,
            status,
            client,
            clientVersion,
            lakonaVersion,
            diagnostics ?? [])
        {
            ServerPath = serverPath,
            ClientPath = clientPath
        };
    }

    private sealed record ProjectLayout(string? SharedDirectory, string? ServerDirectory, string? ClientDirectory);

    private sealed record LayoutPath(string FullPath, string RelativePath)
    {
        public static LayoutPath Create(
            string rootPath,
            string? directory,
            string defaultRelativePath,
            string relativePath)
        {
            var fullPath = directory is null
                ? Resolve(rootPath, defaultRelativePath)
                : Resolve(directory, relativePath);
            return new LayoutPath(fullPath, Path.GetRelativePath(rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/'));
        }
    }
}
