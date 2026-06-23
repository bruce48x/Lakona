namespace Lakona.Tool.Server;

internal interface IServerPackageWriter
{
    Task<string> PackAsync(ServerPackOptions options, CancellationToken cancellationToken = default);
}
