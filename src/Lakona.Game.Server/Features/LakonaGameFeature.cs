using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lakona.Game.Server.Features;

/// <summary>
/// Base class for stable process-level game-server features.
/// </summary>
/// <remarks>
/// Stable features participate in host startup, dependency registration, and
/// cluster feature discovery. Reloadable game behavior belongs in hotfix
/// features rather than in instances of this type.
/// </remarks>
public abstract class LakonaGameFeature
{
    /// <summary>
    /// Gets a value indicating whether the feature is published as a cluster-discoverable node capability.
    /// </summary>
    public virtual bool Discoverable => true;

    /// <summary>
    /// Gets low-cardinality metadata published with the feature when it is discoverable.
    /// </summary>
    /// <remarks>
    /// Metadata must describe stable node capability, not per-player, per-room,
    /// or other high-cardinality runtime state.
    /// </remarks>
    public virtual IReadOnlyDictionary<string, string> Metadata => new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Registers dependencies required by this feature before the host is built.
    /// </summary>
    /// <param name="context">The feature configuration context.</param>
    /// <remarks>
    /// This hook should only register services and options. It should not perform
    /// network, database, or other runtime I/O.
    /// </remarks>
    public virtual void ConfigureServices(LakonaGameFeatureContext context)
    {
    }

    /// <summary>
    /// Starts feature-owned runtime work after the host is built.
    /// </summary>
    /// <param name="context">The feature startup context.</param>
    /// <param name="cancellationToken">A token that cancels startup.</param>
    /// <returns>A task-like value that completes when startup work finishes.</returns>
    /// <remarks>
    /// Client listeners are started after enabled features finish startup. If a
    /// feature fails, host startup fails and already-started features are stopped
    /// in reverse order.
    /// </remarks>
    public virtual ValueTask StartAsync(
        LakonaGameFeatureContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }

    /// <summary>
    /// Stops feature-owned runtime work during host shutdown.
    /// </summary>
    /// <param name="context">The feature shutdown context.</param>
    /// <param name="cancellationToken">A token that requests shutdown cancellation.</param>
    /// <returns>A task-like value that completes when shutdown work finishes.</returns>
    /// <remarks>
    /// Features are stopped in reverse startup order.
    /// </remarks>
    public virtual ValueTask StopAsync(
        LakonaGameFeatureContext context,
        CancellationToken cancellationToken = default)
    {
        return default;
    }
}
