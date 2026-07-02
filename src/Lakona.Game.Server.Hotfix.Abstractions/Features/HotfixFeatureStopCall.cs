namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Provides arguments to a hotfix feature <c>StopAsync</c> lifecycle method.
/// </summary>
public sealed class HotfixFeatureStopCall
{
    /// <summary>
    /// Initializes a new stop call.
    /// </summary>
    /// <param name="featureName">The hotfix feature name.</param>
    /// <param name="state">The feature state retained across reloads.</param>
    /// <param name="services">The current hotfix service provider.</param>
    /// <param name="cancellationToken">A token that requests shutdown cancellation.</param>
    public HotfixFeatureStopCall(
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
    public HotfixFeatureState State { get; }

    /// <summary>
    /// Gets the current hotfix service provider.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets a token that requests feature shutdown cancellation.
    /// </summary>
    /// <remarks>
    /// Cleanup that must run, such as destroying feature-owned timers, should
    /// normally use <see cref="CancellationToken.None"/>.
    /// </remarks>
    public CancellationToken CancellationToken { get; }
}
