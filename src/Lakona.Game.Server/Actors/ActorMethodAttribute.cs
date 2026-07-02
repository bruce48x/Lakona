namespace Lakona.Game.Server.Actors;

/// <summary>
/// Pins the public actor method name used by generated references and wire contracts.
/// </summary>
/// <remarks>
/// Use this when a hotfix behavior method or generated actor contract method
/// needs a stable protocol name independent of the C# method name.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ActorMethodAttribute : Attribute
{
    /// <summary>
    /// Initializes a new actor-method attribute.
    /// </summary>
    /// <param name="name">The stable method name.</param>
    public ActorMethodAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    /// Gets the stable method name.
    /// </summary>
    public string Name { get; }
}
