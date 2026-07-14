namespace Lakona.Tool.Execution;

internal sealed class ToolFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
}
