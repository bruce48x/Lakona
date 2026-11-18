using ICSharpCode.SharpZipLib.Zip;

namespace Lakona.ProjectSystem.Packaging.Server;

internal static class LinuxServerArchive
{
    private const UnixFileMode DefaultFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode Execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    public static void Create(string root, string destination, string entryAssembly, CancellationToken cancellationToken)
    {
        var appHost = Path.GetFileNameWithoutExtension(entryAssembly);
        using var archive = new ZipOutputStream(File.Open(destination, FileMode.CreateNew, FileAccess.Write));
        foreach (var item in new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetRelativePath(root, item.FullName).Replace(Path.DirectorySeparatorChar, '/');
            var directory = item is DirectoryInfo;
            var mode = OperatingSystem.IsWindows() ? DefaultFileMode : File.GetUnixFileMode(item.FullName);
            if ((directory && OperatingSystem.IsWindows()) || name == appHost || name == "createdump")
            {
                mode |= Execute;
            }

            // The upper 16 bits of a Unix ZIP entry contain its file type and mode.
            var fileType = directory ? 0x4000 : 0x8000;
            var entry = new ZipEntry(directory ? name + "/" : name)
            {
                HostSystem = (int)HostSystemID.Unix,
                ExternalFileAttributes = (fileType | (int)mode) << 16,
                DateTime = item.LastWriteTime,
                Size = item is FileInfo file ? file.Length : 0
            };
            archive.PutNextEntry(entry);
            if (!directory)
            {
                using var input = File.OpenRead(item.FullName);
                input.CopyTo(archive);
            }
            archive.CloseEntry();
        }
    }
}
