namespace Lakona.Tool.Server;

internal interface IHotfixPackageBuilder
{
    Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        CancellationToken cancellationToken);
}
