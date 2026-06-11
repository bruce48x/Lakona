using System.IO.Compression;
using System.Text;
using Lakona.Tool.Planning;

namespace Lakona.Tool.Execution;

internal sealed class TransactionalOutputWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task WriteAsync(GenerationPlan plan, CancellationToken cancellationToken)
    {
        var targetRoot = Path.GetFullPath(plan.RootPath);
        if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            throw new InvalidOperationException($"Target directory already exists and is not empty: {targetRoot}");
        }

        var parentPath = Path.GetDirectoryName(targetRoot);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            throw new InvalidOperationException($"Unable to determine parent directory for target path: {targetRoot}");
        }

        Directory.CreateDirectory(parentPath);
        var stagingRoot = Path.Combine(parentPath, $".{Path.GetFileName(targetRoot)}.tmp-{Guid.NewGuid():N}");
        var stagingRootFullPath = Path.GetFullPath(stagingRoot);
        Directory.CreateDirectory(stagingRootFullPath);

        try
        {
            foreach (var directory in plan.Directories)
            {
                Directory.CreateDirectory(ResolveStagingPath(stagingRootFullPath, directory.RelativePath));
            }

            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = ResolveStagingPath(stagingRootFullPath, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? stagingRootFullPath);
                await File.WriteAllTextAsync(fullPath, NormalizeText(file.Content), Utf8NoBom, cancellationToken).ConfigureAwait(false);
            }

            foreach (var archive in plan.Archives ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExtractArchive(stagingRootFullPath, archive);
            }

            if (Directory.Exists(targetRoot))
            {
                Directory.Delete(targetRoot, recursive: true);
            }

            Directory.Move(stagingRootFullPath, targetRoot);
        }
        catch (Exception generationError)
        {
            if (Directory.Exists(stagingRootFullPath))
            {
                try
                {
                    Directory.Delete(stagingRootFullPath, recursive: true);
                }
                catch (Exception cleanupError)
                {
                    throw new InvalidOperationException(
                        $"Project generation failed: {generationError.Message}{Environment.NewLine}Cleanup of staging directory '{stagingRootFullPath}' also failed: {cleanupError.Message}",
                        new AggregateException(generationError, cleanupError));
                }
            }

            throw;
        }
    }

    private static string ResolveStagingPath(string stagingRootFullPath, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(stagingRootFullPath, relativePath));
        var stagingRootWithSeparator = EnsureTrailingSeparator(stagingRootFullPath);
        if (!fullPath.Equals(stagingRootFullPath, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(stagingRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Generated file escapes project root: {relativePath}");
        }

        return fullPath;
    }

    private static void ExtractArchive(string stagingRootFullPath, GeneratedArchive archive)
    {
        var destinationRoot = ResolveStagingPath(stagingRootFullPath, archive.RelativeDestinationPath);
        Directory.CreateDirectory(destinationRoot);

        using var stream = typeof(TransactionalOutputWriter).Assembly.GetManifestResourceStream(archive.ResourceName)
            ?? throw new InvalidOperationException($"Embedded generated archive not found: {archive.ResourceName}");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var destinationRootWithSeparator = EnsureTrailingSeparator(Path.GetFullPath(destinationRoot));
        foreach (var entry in zip.Entries)
        {
            var fullPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!fullPath.StartsWith(destinationRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Embedded generated archive entry escapes destination: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(fullPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? destinationRoot);
            entry.ExtractToFile(fullPath, overwrite: true);
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string NormalizeText(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
        return normalized.EndsWith('\n') ? normalized : normalized + "\n";
    }
}
