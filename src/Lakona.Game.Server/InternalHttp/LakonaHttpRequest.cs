namespace Lakona.Game.Server.InternalHttp;

public sealed record LakonaHttpRequest(
    string Method,
    string Path,
    Stream Body,
    bool RemoteAddressIsLoopback);
