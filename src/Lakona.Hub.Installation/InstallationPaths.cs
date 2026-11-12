namespace Lakona.Hub.Installation;

public sealed class InstallationPaths
{
    public InstallationPaths(string localAppData)
    {
        var root = Path.GetFullPath(localAppData);
        Application = Path.Combine(root, "Programs", "Lakona Hub");
        State = Path.Combine(root, "Lakona", "Hub", "installer");
    }

    public static InstallationPaths Current => new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public string Application { get; }
    public string State { get; }
    public string Staging => Path.Combine(State, "staging");
    public string Backup => Path.Combine(State, "backup");
    public string Journal => Path.Combine(State, "journal.json");
    public string Health => Path.Combine(State, "health.json");
    public string Executable => Path.Combine(Application, "Lakona.Hub.exe");
    public string Updater => Path.Combine(Application, "Lakona.Hub.Updater.exe");

    public bool Owns(string directory) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), Application, StringComparison.OrdinalIgnoreCase);

    public void Prepare()
    {
        RejectLinks(Application);
        RejectLinks(State);
        Directory.CreateDirectory(State);
        Directory.CreateDirectory(Path.GetDirectoryName(Application)!);
    }

    public FileStream AcquireLock()
    {
        Prepare();
        try { return new FileStream(Path.Combine(State, "lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Another Lakona Hub installation or update is running.", ex); }
    }

    public void RemoveWorkDirectory(string path)
    {
        var root = State;
        var full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase) ||
            !(Path.GetFileName(full).StartsWith("setup-", StringComparison.Ordinal) || Path.GetFileName(full).StartsWith("handoff-", StringComparison.Ordinal)))
            throw new InvalidOperationException("The temporary directory is outside the installer state root.");
        RejectLinks(full);
        if (!Directory.Exists(full)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(full, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })) RejectLinks(entry);
        Directory.Delete(full, recursive: true);
    }

    public void CleanupCompletedWork()
    {
        Prepare();
        var candidates = Directory.EnumerateDirectories(State)
            .Where(directory => Path.GetFileName(directory).StartsWith("setup-", StringComparison.Ordinal) || Path.GetFileName(directory).StartsWith("handoff-", StringComparison.Ordinal))
            .OrderByDescending(Directory.GetLastWriteTimeUtc).ToArray();
        for (var index = 0; index < candidates.Length; index++)
        {
            var directory = candidates[index];
            if (index < 2 && !File.Exists(Path.Combine(directory, "completed")) && Directory.GetLastWriteTimeUtc(directory) > DateTime.UtcNow.AddDays(-7)) continue;
            try
            {
                RejectLinks(directory);
                var lease = Path.Combine(directory, "in-use");
                RejectLinks(lease);
                // An active bootstrap, Hub handoff, or updater holds a shared read
                // lease. Only abandoned/completed work can be opened exclusively.
                using (new FileStream(lease, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                RemoveWorkDirectory(directory);
            }
            catch (IOException) { } // A finishing process may still hold its native DLLs.
            catch (UnauthorizedAccessException) { }
        }
    }

    public FileStream? HoldWork(string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!string.Equals(Path.GetDirectoryName(full), State, StringComparison.OrdinalIgnoreCase)) return null;
        if (!Path.GetFileName(full).StartsWith("setup-", StringComparison.Ordinal) && !Path.GetFileName(full).StartsWith("handoff-", StringComparison.Ordinal)) return null;
        RejectLinks(full);
        var lease = Path.Combine(full, "in-use");
        RejectLinks(lease);
        if (!File.Exists(lease)) using (File.Create(lease)) { }
        return new FileStream(lease, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    internal void RemoveTree(string path)
    {
        if (path != Application && path != Staging && path != Backup)
            throw new InvalidOperationException("The directory is not owned by this installation.");
        RejectLinks(path);
        if (!Directory.Exists(path)) return;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            RejectLinks(entry);
        Directory.Delete(path, recursive: true);
    }

    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked installation paths are not supported.");
    }

    internal static string PayloadPath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') ||
            relative.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsDeviceName(part)))
            throw new InvalidDataException("The package contains an unsafe path.");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package escapes its installation directory.");
        return path;
    }

    private static bool IsDeviceName(string part)
    {
        var name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            (name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && char.IsDigit(name[3]));
    }
}
