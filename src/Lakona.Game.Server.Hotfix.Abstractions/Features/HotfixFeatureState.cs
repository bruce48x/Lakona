namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Stores feature-scoped runtime state retained across hotfix reloads.
/// </summary>
/// <remarks>
/// Use this for handles owned by feature lifecycle, such as timer ids. Do not
/// store old hotfix assembly types, delegates, or service instances that would
/// keep an unloaded hotfix generation alive.
/// </remarks>
public sealed class HotfixFeatureState
{
    /// <summary>
    /// Gets the mutable feature state bag.
    /// </summary>
    public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
