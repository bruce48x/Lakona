namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks an interface as the generated contract for a stable actor type.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class HotfixActorContractAttribute : Attribute
{
    /// <summary>
    /// Initializes a new actor contract attribute.
    /// </summary>
    /// <param name="actorType">The actor type represented by the contract.</param>
    public HotfixActorContractAttribute(Type actorType)
    {
        ActorType = actorType ?? throw new ArgumentNullException(nameof(actorType));
    }

    /// <summary>
    /// Gets the actor type represented by the contract.
    /// </summary>
    public Type ActorType { get; }
}
