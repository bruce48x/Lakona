namespace Lakona.Game.Server.Actors;

/// <summary>
/// Excludes a method from generated actor reference contracts.
/// </summary>
/// <remarks>
/// Apply this to helper methods that are public for ordinary C# composition but
/// should not become remotely callable actor behavior.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ActorIgnoreAttribute : Attribute
{
}
