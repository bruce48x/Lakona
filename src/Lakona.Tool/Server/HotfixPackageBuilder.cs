using Lakona.Tool.Hotfix;

namespace Lakona.Tool.Server;

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
