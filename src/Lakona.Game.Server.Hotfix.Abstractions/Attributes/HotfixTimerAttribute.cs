namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks a generation-scoped hotfix module whose public instance methods are
/// timer callback entries.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixTimerAttribute : Attribute
{
}
