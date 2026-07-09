namespace Lakona.Game.Server.Health;

public sealed record LakonaHealthHttpRequest(
    string Method,
    string Path,
    bool RemoteAddressIsLoopback,
    bool RequireLoopback = true);
