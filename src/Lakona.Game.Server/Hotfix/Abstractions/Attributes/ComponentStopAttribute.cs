namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>Marks final component shutdown, after its callers stop. Ordinary reload does not invoke it.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ComponentStopAttribute : Attribute { }
