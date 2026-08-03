namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks the hotfix startup declaration type scanned by the runtime.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixStartupAttribute : Attribute
{
}
