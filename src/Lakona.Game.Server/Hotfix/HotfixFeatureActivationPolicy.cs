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

        var scannedByName = scannedFeatures.ToDictionary(static feature => feature.Name, StringComparer.OrdinalIgnoreCase);
        var selected = new List<HotfixFeatureDeclaration>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _options.Feature)
        {
            if (string.IsNullOrWhiteSpace(name) || !added.Add(name))
            {
                continue;
            }

            if (!scannedByName.TryGetValue(name, out var feature))
            {
                throw new InvalidOperationException($"Configured hotfix feature '{name}' was not found in the candidate runtime.");
            }

            selected.Add(feature);
        }

        return selected;
    }
}
