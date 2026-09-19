namespace Lakona.ProjectSystem.Generation.Execution;

internal sealed class RestoredUnityDependencies(
    string rootPath,
    string? cleanupRoot = null,
    string? editorVersion = null,
    string? editorRevision = null) : IDisposable
{
    public string RootPath { get; } = rootPath;
    public string? EditorVersion { get; } = editorVersion;
    public string? EditorRevision { get; } = editorRevision;
    private string CleanupRoot { get; } = cleanupRoot ?? rootPath;

    public void Dispose()
    {
        if (Directory.Exists(CleanupRoot))
        {
            Directory.Delete(CleanupRoot, recursive: true);
        }
    }
}
