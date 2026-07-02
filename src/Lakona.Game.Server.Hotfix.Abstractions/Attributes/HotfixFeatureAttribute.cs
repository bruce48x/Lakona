namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Declares the stable name of a reloadable hotfix feature.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HotfixFeatureAttribute : Attribute
{
    /// <summary>
    /// Initializes a new hotfix feature attribute.
    /// </summary>
    /// <param name="name">The feature name used by configuration, lifecycle, and cluster discovery.</param>
    public HotfixFeatureAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// Gets the stable feature name.
    /// </summary>
    public string Name { get; }
}
