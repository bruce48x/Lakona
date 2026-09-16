namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks a Hotfix class whose implemented lifecycle interfaces are discovered and
/// bound automatically. Contract methods use HotfixLifecycleCall&lt;TRequest&gt;
/// and are called directly through their interfaces within a generation lease.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HotfixLifecycleAttribute : Attribute;
