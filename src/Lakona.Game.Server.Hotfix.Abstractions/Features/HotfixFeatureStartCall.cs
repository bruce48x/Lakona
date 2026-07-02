namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Provides arguments to a hotfix feature <c>StartAsync</c> lifecycle method.
/// </summary>
public sealed class HotfixFeatureStartCall
{
    /// <summary>
    /// Initializes a new start call.
    /// </summary>
    /// <param name="featureName">The hotfix feature name.</param>
    /// <param name="state">The feature state retained across reloads.</param>
    /// <param name="services">The current hotfix service provider.</param>
    /// <param name="cancellationToken">A token that cancels startup.</param>
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

    /// <summary>
    /// Gets the hotfix feature name.
    /// </summary>
    public string FeatureName { get; }

    /// <summary>
    /// Gets feature state retained across hotfix reloads.
    /// </summary>
    /// <remarks>
    /// Store handles such as <see cref="Timers.TimerId"/> here when feature
    /// shutdown must destroy them later.
    /// </remarks>
    public HotfixFeatureState State { get; }

    /// <summary>
    /// Gets the current hotfix service provider.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets a token that cancels feature startup.
    /// </summary>
    public CancellationToken CancellationToken { get; }
}
