namespace Lakona.ProjectSystem.Packaging.Server;

internal interface IHotfixPackageBuilder
{
    Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        string publishedHostDirectory,
        string runtimeIdentifier,
        CancellationToken cancellationToken);
}
