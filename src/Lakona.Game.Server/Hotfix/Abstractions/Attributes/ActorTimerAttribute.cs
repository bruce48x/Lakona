namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>Marks a Behavior method as an activation-owned timer callback, not a remote Actor API.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ActorTimerAttribute : Attribute;
