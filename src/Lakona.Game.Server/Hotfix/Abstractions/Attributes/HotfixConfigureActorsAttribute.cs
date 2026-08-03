namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks the hotfix startup method that declares actor startup and placement policies.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HotfixConfigureActorsAttribute : Attribute
{
}
