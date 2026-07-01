using Lakona.Game.Server.Hotfix.Abstractions;

namespace Lakona.Game.Server.Hotfix;

public interface IHotfixFeatureActivationPolicy
{
    IReadOnlyList<HotfixFeatureDeclaration> SelectActiveFeatures(
        IReadOnlyList<HotfixFeatureDeclaration> scannedFeatures);
}

internal sealed class AllHotfixFeaturesActivationPolicy : IHotfixFeatureActivationPolicy
{
    public static AllHotfixFeaturesActivationPolicy Instance { get; } = new();

    public IReadOnlyList<HotfixFeatureDeclaration> SelectActiveFeatures(
        IReadOnlyList<HotfixFeatureDeclaration> scannedFeatures)
    {
        ArgumentNullException.ThrowIfNull(scannedFeatures);
        return scannedFeatures;
    }
}
