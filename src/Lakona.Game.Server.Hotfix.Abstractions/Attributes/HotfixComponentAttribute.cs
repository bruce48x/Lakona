namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks a dependency-only helper that is activated once per hotfix generation.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HotfixComponentAttribute : Attribute
{
}
