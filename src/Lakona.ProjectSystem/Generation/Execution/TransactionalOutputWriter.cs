using System.IO.Compression;
using System.Text;
using Lakona.ProjectSystem;
using Lakona.ProjectSystem.Generation.Planning;

namespace Lakona.ProjectSystem.Generation.Execution;

internal sealed class TransactionalOutputWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public Task WriteAsync(GenerationPlan plan, CancellationToken cancellationToken) =>
        WriteAsync(plan, restoredUnityPackagesPath: null, cancellationToken);

    public async Task WriteAsync(GenerationPlan plan, string? restoredUnityPackagesPath, CancellationToken cancellationToken)
    {
        var targetRoot = Path.GetFullPath(plan.RootPath);
        if (Directory.Exists(targetRoot) && Directory.EnumerateFileSystemEntries(targetRoot).Any())
        {
            throw new LakonaProjectCreationException($"Target directory already exists and is not empty: {targetRoot}");
        }

        var parentPath = Path.GetDirectoryName(targetRoot);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            throw new LakonaProjectCreationException($"Unable to determine the parent directory for: {targetRoot}");
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

            if (restoredUnityPackagesPath is not null)
            {
                CopyDirectory(
                    restoredUnityPackagesPath,
                    ResolveStagingPath(stagingRootFullPath, "Client/Assets/Packages"),
                    cancellationToken);
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
                    throw new LakonaProjectCreationException(
                        $"Project generation failed: {generationError.Message}. Cleanup also failed for {stagingRootFullPath}: {cleanupError.Message}",
                        new AggregateException(generationError, cleanupError));
                }
            }

            throw;
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceRoot))
        {
            throw new LakonaProjectCreationException($"Restored Unity package directory was not found: {sourceRoot}");
        }

        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationRoot);
            File.Copy(sourcePath, destinationPath, overwrite: false);
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
