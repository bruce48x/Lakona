namespace Lakona.Game.Server.Actors;

/// <summary>
/// Excludes a method from generated actor reference contracts.
/// </summary>
/// <remarks>
/// Apply this to helper methods that are public for ordinary C# composition but
/// should not become remotely callable actor behavior.
/// This attribute cannot be combined with <see cref="ActorMethodAttribute"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ActorIgnoreAttribute : Attribute
{
}
