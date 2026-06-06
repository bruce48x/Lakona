namespace Lakona.Rpc.Starter;

internal static class StarterOutputManager
{
    public static void GenerateIntoTargetDirectory(string targetRootPath, Action<string> generate)
    {
        if (Directory.Exists(targetRootPath) && Directory.EnumerateFileSystemEntries(targetRootPath).Any())
        {
            throw new InvalidOperationException($"Target directory already exists and is not empty: {targetRootPath}");
        }

        var parentPath = Path.GetDirectoryName(targetRootPath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            throw new InvalidOperationException($"Unable to determine parent directory for target path: {targetRootPath}");
        }

        Directory.CreateDirectory(parentPath);

        var stagingRootPath = Path.Combine(parentPath, $".{Path.GetFileName(targetRootPath)}.tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRootPath);

        try
        {
            generate(stagingRootPath);

            if (Directory.Exists(targetRootPath))
            {
                Directory.Delete(targetRootPath, recursive: true);
            }

            Directory.Move(stagingRootPath, targetRootPath);
        }
        catch (Exception generationError)
        {
            if (Directory.Exists(stagingRootPath))
            {
                try
                {
                    Directory.Delete(stagingRootPath, recursive: true);
                }
                catch (Exception cleanupError)
                {
                    throw new InvalidOperationException(
                        $"Project generation failed: {generationError.Message}{Environment.NewLine}Cleanup of staging directory '{stagingRootPath}' also failed: {cleanupError.Message}",
                        new AggregateException(generationError, cleanupError));
                }
            }

            throw;
        }
    }
}
