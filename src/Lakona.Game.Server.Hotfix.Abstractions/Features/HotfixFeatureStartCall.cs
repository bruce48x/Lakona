namespace Lakona.Game.Server.Hotfix.Abstractions;

public sealed class HotfixFeatureStartCall
{
    public HotfixFeatureStartCall(
        string featureName,
        HotfixFeatureState state,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
        FeatureName = featureName;
        State = state ?? throw new ArgumentNullException(nameof(state));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        CancellationToken = cancellationToken;
    }

    public string FeatureName { get; }

    public HotfixFeatureState State { get; }

    public IServiceProvider Services { get; }

    public CancellationToken CancellationToken { get; }
}
