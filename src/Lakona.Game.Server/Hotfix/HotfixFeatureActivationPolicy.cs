using Lakona.Game.Server.Configuration;
using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

internal sealed class HotfixFeatureActivationPolicy : IHotfixFeatureActivationPolicy
{
    private readonly LakonaGameRuntimeOptions _options;

    public HotfixFeatureActivationPolicy(LakonaGameRuntimeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<HotfixFeatureDeclaration> SelectActiveFeatures(
        IReadOnlyList<HotfixFeatureDeclaration> scannedFeatures)
    {
        ArgumentNullException.ThrowIfNull(scannedFeatures);
        if (_options.Feature is null)
        {
            return scannedFeatures;
        }

        var allowed = _options.Feature.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return scannedFeatures
            .Where(feature => allowed.Contains(feature.Name))
            .ToArray();
    }
}
