namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Binds a hotfix behavior class to a stable actor type.
/// </summary>
/// <remarks>
/// Behavior methods execute inside the target actor turn and may mutate the
/// actor's stable state. Long-lived runtime handles should be owned by actor
/// startup or application services instead of static behavior state.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixBehaviorOfAttribute : Attribute
{
    /// <summary>
    /// Initializes a new behavior binding attribute.
    /// </summary>
    /// <param name="actorType">The stable actor type this behavior extends.</param>
    public HotfixBehaviorOfAttribute(Type actorType)
    {
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
    }

    /// <summary>
    /// Gets the stable actor type this behavior extends.
    /// </summary>
    public Type ActorType { get; }
}
