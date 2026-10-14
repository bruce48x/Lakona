namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>Marks the component's first activation: public instance ValueTask(CancellationToken).</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ComponentStartAttribute : Attribute { }
