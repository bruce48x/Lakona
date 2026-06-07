using System.IO.Compression;
using System.Text;

namespace Lakona.Tool.RpcStarter;

internal static class StarterFileWriter
{
    public static void ExtractEmbeddedZip(string resourceName, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        using var stream = typeof(StarterFileWriter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded template asset not found: {resourceName}");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var rootPath = Path.GetFullPath(destinationDirectory);
        foreach (var entry in archive.Entries)
        {
            var entryPath = Path.Combine(destinationDirectory, entry.FullName);
            var fullPath = Path.GetFullPath(entryPath);
            if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Embedded template asset contains an invalid path: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    public static void Write(string path, string content)
    {
        var normalized = content.Replace("\r\n", "\n").TrimStart('\ufeff');
        if (!normalized.EndsWith('\n'))
        {
            normalized += "\n";
        }

        File.WriteAllText(path, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
