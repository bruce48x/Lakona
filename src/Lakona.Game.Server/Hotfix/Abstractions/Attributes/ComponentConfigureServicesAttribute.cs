namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks an optional public static void method on a HotfixComponent that
/// registers node-owned services before the root provider is built.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ComponentConfigureServicesAttribute : Attribute
{
}
