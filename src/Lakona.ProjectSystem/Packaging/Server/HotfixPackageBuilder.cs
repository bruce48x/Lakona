using Lakona.ProjectSystem.Packaging.Hotfix;

namespace Lakona.ProjectSystem.Packaging.Server;

internal sealed class HotfixPackageBuilder : IHotfixPackageBuilder
{
    public Task<string> PackAsync(
        string projectPath,
        string outputDirectory,
        string configuration,
        string version,
        CancellationToken cancellationToken)
    {
        return new HotfixPackageWriter().PackAsync(
            projectPath,
            outputDirectory,
            configuration,
            version,
            cancellationToken);
    }
}
