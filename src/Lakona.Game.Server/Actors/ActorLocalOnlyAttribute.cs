namespace Lakona.Game.Server.Actors;

/// <summary>
/// Marks an actor type as unavailable for generated remote actor access.
/// </summary>
/// <remarks>
/// Local-only actors can still be hosted and called inside the current process,
/// but generated APIs must not expose cross-node selectors for them.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ActorLocalOnlyAttribute : Attribute
{
}
