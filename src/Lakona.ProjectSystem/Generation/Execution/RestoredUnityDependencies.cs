namespace Lakona.ProjectSystem.Generation.Execution;

internal sealed class RestoredUnityDependencies(string rootPath, string? cleanupRoot = null) : IDisposable
{
    public string RootPath { get; } = rootPath;
    private string CleanupRoot { get; } = cleanupRoot ?? rootPath;

    public void Dispose()
    {
        if (Directory.Exists(CleanupRoot))
        {
            Directory.Delete(CleanupRoot, recursive: true);
        }
    }
}
