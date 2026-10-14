namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks stable, root-owned state in Server.App for a Hotfix component.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ComponentStateAttribute : Attribute { }
