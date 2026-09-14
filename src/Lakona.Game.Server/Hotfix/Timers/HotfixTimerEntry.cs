namespace Lakona.Game.Server.Hotfix.Timers;

/// <summary>
/// Identifies a selected hotfix timer callback without retaining a delegate
/// or an object from the hotfix assembly.
/// </summary>
internal readonly record struct HotfixTimerEntry<TArgs>(
    string CallbackFullName,
    string MethodName,
    ulong MethodId);
