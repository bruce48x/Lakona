namespace Lakona.Game.Server.Actors;

/// <summary>
/// Pins the public actor name used by generated references and wire contracts.
/// </summary>
/// <remarks>
/// Use this when the C# actor type name should be refactored without changing
/// actor ids, generated contract names, or long-lived protocol identity.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ActorNameAttribute : Attribute
{
    /// <summary>
    /// Initializes a new actor-name attribute.
    /// </summary>
    /// <param name="name">The stable actor name.</param>
    public ActorNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// Gets the stable actor name.
    /// </summary>
    public string Name { get; }
}
