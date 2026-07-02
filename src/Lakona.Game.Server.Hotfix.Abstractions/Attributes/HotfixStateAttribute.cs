namespace Lakona.Game.Server.Hotfix.Abstractions;

/// <summary>
/// Marks a type as stable state used by hotfix-generated accessors or serializers.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class HotfixStateAttribute : Attribute
{
}
