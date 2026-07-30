namespace Lakona.ProjectSystem.Packaging.Server;

internal interface IServerPackageWriter
{
    Task<string> PackAsync(ServerPackOptions options, CancellationToken cancellationToken = default);
}
