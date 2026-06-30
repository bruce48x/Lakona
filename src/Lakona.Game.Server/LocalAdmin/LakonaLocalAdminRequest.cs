namespace Lakona.Game.Server.LocalAdmin;

public sealed record LakonaLocalAdminRequest(
    string Method,
    string Path,
    string Body,
    bool RemoteAddressIsLoopback);
