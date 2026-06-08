namespace Lakona.Tool.RpcStarter;

internal static class StarterFileWriter
{
    public static void ExtractEmbeddedZip(string resourceName, string destinationDirectory)
    {
        ToolFileWriter.ExtractEmbeddedZip(resourceName, destinationDirectory);
    }

    public static void Write(string path, string content)
    {
        ToolFileWriter.WriteText(path, content);
    }
}
