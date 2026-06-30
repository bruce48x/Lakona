namespace Lakona.Game.Server.LocalAdmin;

public sealed record LakonaLocalAdminRequest(
    string Method,
    string Path,
    Stream Body,
    bool RemoteAddressIsLoopback,
    bool RequireLoopback = true)
{
    public LakonaLocalAdminRequest(
        string method,
        string path,
        string body,
        bool remoteAddressIsLoopback)
        : this(
            method,
            path,
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body)),
            remoteAddressIsLoopback)
    {
    }
}
