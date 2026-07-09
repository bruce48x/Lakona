namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks the hotfix startup method that registers hotfix-scoped services.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class HotfixConfigureServicesAttribute : Attribute
{
}
