using Lakona.ProjectSystem.Packaging.Hotfix;

namespace Lakona.ProjectSystem.Packaging.Server;

internal sealed class HotfixPackageBuilder(IDotNetCommandRunner dotNet) : IHotfixPackageBuilder
{
    public Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        string publishedHostDirectory,
        string runtimeIdentifier,
        CancellationToken cancellationToken)
    {
        return new HotfixPackageWriter(dotNet).PackAsync(
            projectPath,
            outputDirectory,
            configuration,
            version,
            cancellationToken,
            publishedHostDirectory: publishedHostDirectory,
            runtimeIdentifier: runtimeIdentifier);
    }
}
