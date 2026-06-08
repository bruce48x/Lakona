using System.IO.Compression;
using System.Text;

internal static class ToolFileWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteText(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, NormalizeText(content), Utf8NoBom);
    }

    public static Task WriteTextAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        return File.WriteAllTextAsync(path, NormalizeText(content), Utf8NoBom);
    }

    public static void WriteTextIfMissing(string path, string content)
    {
        if (File.Exists(path))
        {
            return;
        }

        WriteText(path, content);
    }

    public static Task WriteTextIfMissingAsync(string path, string content)
    {
        return File.Exists(path) ? Task.CompletedTask : WriteTextAsync(path, content);
    }

    public static void ExtractEmbeddedZip(string resourceName, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        using var stream = typeof(ToolFileWriter).Assembly.GetManifestResourceStream(resourceName)
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

    private static string NormalizeText(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('﻿');
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }
}
