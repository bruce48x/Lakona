namespace Lakona.Game.Server.Hotfix.Abstractions.Timers;

/// <summary>
/// Identifies a generated hotfix timer callback without retaining a delegate
/// or an object from the hotfix assembly.
/// </summary>
public readonly record struct HotfixTimerEntry<TArgs>(
    string CallbackFullName,
    string MethodName,
    ulong MethodId);
