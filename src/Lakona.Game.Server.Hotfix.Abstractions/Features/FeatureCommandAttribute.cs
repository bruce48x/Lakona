namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Assigns a stable numeric identity to a feature command request type.
/// </summary>
/// <remarks>
/// The id is used as the cluster wire identity for the command. Keep it stable
/// once clients or other server nodes can send the command.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class FeatureCommandAttribute : Attribute
{
    /// <summary>
    /// Initializes a new feature command attribute.
    /// </summary>
    /// <param name="id">The positive command id.</param>
    public FeatureCommandAttribute(int id)
    {
        Id = id;
    }

    /// <summary>
    /// Gets the stable command id.
    /// </summary>
    public int Id { get; }
}
