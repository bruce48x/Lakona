namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks a concrete helper reconstructed each Hotfix generation. Optional ComponentStart/Stop
/// hooks run at first activation and final shutdown, not on ordinary reload. Stable state
/// is constructor-injected from the application root. Background work requires ActorTimer.
/// Do not apply to exceptions: construct them with runtime data using new.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HotfixComponentAttribute : Attribute
{
}
